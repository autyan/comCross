using System.Security.Cryptography;
using System.Text.Json;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Entry point for plugin package signature/trust verification.
/// </summary>
public sealed class PluginSignatureVerificationService
{
    public const string SignatureFileName = "ComCross.Plugin.Signature.json";
    public const string SupportedAlgorithm = "RSA-PSS-SHA256";
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Func<PluginSignatureVerificationSettings> _policyAccessor;
    private readonly IPluginTrustKeyProvider _keyProvider;
    private readonly ILogger<PluginSignatureVerificationService> _logger;

    public PluginSignatureVerificationService(
        SettingsService settings,
        IPluginTrustKeyProvider keyProvider,
        ILogger<PluginSignatureVerificationService> logger)
        : this(() => settings.Current.Plugins.SignatureVerification, keyProvider, logger)
    {
    }

    public PluginSignatureVerificationService(
        PluginSignatureVerificationSettings settings,
        IPluginTrustKeyProvider keyProvider,
        ILogger<PluginSignatureVerificationService> logger)
        : this(() => settings, keyProvider, logger)
    {
    }

    private PluginSignatureVerificationService(
        Func<PluginSignatureVerificationSettings> policyAccessor,
        IPluginTrustKeyProvider keyProvider,
        ILogger<PluginSignatureVerificationService> logger)
    {
        _policyAccessor = policyAccessor ?? throw new ArgumentNullException(nameof(policyAccessor));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsTrusted(PluginInfo plugin, out string? error)
    {
        error = null;

        var policy = _policyAccessor();
        if (!policy.Enabled)
        {
            return true;
        }

        var id = plugin.Manifest.Id;
        if (!string.IsNullOrWhiteSpace(id)
            && policy.AllowUnsignedPluginIds.Contains(id, StringComparer.Ordinal))
        {
            _logger.LogWarning(
                "Plugin signature verification is enabled, but plugin '{PluginId}' is allow-listed as unsigned (development mode).",
                id);
            return true;
        }

        return VerifySignedPackage(plugin, out error);
    }

    private bool VerifySignedPackage(PluginInfo plugin, out string? error)
    {
        var packageDirectory = Path.GetDirectoryName(plugin.AssemblyPath);
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            error = $"Plugin package directory cannot be resolved: {plugin.Manifest.Id}";
            return false;
        }

        var signaturePath = Path.Combine(packageDirectory, SignatureFileName);
        if (!File.Exists(signaturePath))
        {
            error = $"Plugin package is unsigned: {plugin.Manifest.Id}";
            return false;
        }

        PluginPackageSignature? signature;
        try
        {
            signature = JsonSerializer.Deserialize<PluginPackageSignature>(
                File.ReadAllText(signaturePath),
                JsonOptions);
        }
        catch (Exception ex)
        {
            error = $"Invalid plugin signature file: {ex.Message}";
            return false;
        }

        if (signature is null)
        {
            error = "Invalid plugin signature file.";
            return false;
        }

        if (!ValidateSignatureMetadata(plugin, signature, out error))
        {
            return false;
        }

        if (!_keyProvider.TryGetPublicKey(signature.KeyId, out var publicKey, out var keyError))
        {
            error = keyError ?? $"Unknown plugin signing key id: {signature.KeyId}";
            return false;
        }

        using (publicKey)
        {
            if (!ValidatePackageFiles(packageDirectory, signature, out error))
            {
                return false;
            }

            byte[] signatureBytes;
            try
            {
                signatureBytes = Convert.FromBase64String(signature.Signature);
            }
            catch
            {
                error = "Plugin signature is not valid Base64.";
                return false;
            }

            var payloadBytes = PluginSignatureCanonicalizer.CreatePayloadBytes(signature);
            var ok = publicKey.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            if (!ok)
            {
                error = $"Plugin signature verification failed: {plugin.Manifest.Id}";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateSignatureMetadata(
        PluginInfo plugin,
        PluginPackageSignature signature,
        out string? error)
    {
        if (signature.SchemaVersion != SupportedSchemaVersion)
        {
            error = $"Unsupported plugin signature schema version: {signature.SchemaVersion}";
            return false;
        }

        if (!string.Equals(signature.Algorithm, SupportedAlgorithm, StringComparison.Ordinal))
        {
            error = $"Unsupported plugin signature algorithm: {signature.Algorithm}";
            return false;
        }

        if (!string.Equals(signature.PluginId, plugin.Manifest.Id, StringComparison.Ordinal))
        {
            error = $"Plugin signature id mismatch: expected {plugin.Manifest.Id}, got {signature.PluginId}.";
            return false;
        }

        if (!string.Equals(signature.Version, plugin.Manifest.Version, StringComparison.Ordinal))
        {
            error = $"Plugin signature version mismatch: expected {plugin.Manifest.Version}, got {signature.Version}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(signature.KeyId))
        {
            error = "Plugin signature key id is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(signature.Signature))
        {
            error = "Plugin signature value is missing.";
            return false;
        }

        if (signature.Files.Count == 0)
        {
            error = "Plugin signature file list is empty.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidatePackageFiles(
        string packageDirectory,
        PluginPackageSignature signature,
        out string? error)
    {
        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in signature.Files)
        {
            var relativePath = PluginSignatureCanonicalizer.NormalizeRelativePath(file.Path);
            if (!IsSafeRelativePath(relativePath))
            {
                error = $"Plugin signature contains an unsafe file path: {file.Path}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(file.Sha256))
            {
                error = $"Plugin signature is missing SHA-256 for {relativePath}.";
                return false;
            }

            if (!listed.TryAdd(relativePath, file.Sha256.ToLowerInvariant()))
            {
                error = $"Plugin signature contains duplicate file path: {relativePath}";
                return false;
            }
        }

        var actual = Directory
            .EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)
            .Select(path => PluginSignatureCanonicalizer.NormalizeRelativePath(Path.GetRelativePath(packageDirectory, path)))
            .Where(path => !string.Equals(path, SignatureFileName, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        foreach (var path in actual)
        {
            if (!listed.ContainsKey(path))
            {
                error = $"Plugin package contains unsigned file: {path}";
                return false;
            }
        }

        foreach (var (relativePath, expectedSha256) in listed)
        {
            var fullPath = Path.GetFullPath(Path.Combine(packageDirectory, relativePath));
            var root = Path.GetFullPath(packageDirectory);
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(fullPath, root, StringComparison.Ordinal))
            {
                error = $"Plugin signature path escapes package directory: {relativePath}";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = $"Plugin signature references missing file: {relativePath}";
                return false;
            }

            var actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.Ordinal))
            {
                error = $"Plugin package hash mismatch: {relativePath}";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\'))
        {
            return false;
        }

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(part => part != "." && part != "..");
    }
}
