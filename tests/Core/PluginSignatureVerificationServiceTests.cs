using System.Security.Cryptography;
using System.Text.Json;
using ComCross.Core.Models;
using ComCross.Core.Services;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ComCross.Tests.Core;

public sealed class PluginSignatureVerificationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ComCross.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void IsTrusted_AllowsUnsignedPlugin_WhenPolicyDisabled()
    {
        var plugin = CreateUnsignedPlugin("policy-disabled");
        var service = CreateService(enabled: false, RSA.Create());

        var trusted = service.IsTrusted(plugin, out var error);

        Assert.True(trusted);
        Assert.Null(error);
    }

    [Fact]
    public void IsTrusted_RejectsUnsignedPlugin_WhenPolicyEnabled()
    {
        var plugin = CreateUnsignedPlugin("unsigned");
        var service = CreateService(enabled: true, RSA.Create());

        var trusted = service.IsTrusted(plugin, out var error);

        Assert.False(trusted);
        Assert.Contains("unsigned", error);
    }

    [Fact]
    public void IsTrusted_VerifiesSignedPackage()
    {
        using var rsa = RSA.Create(2048);
        var plugin = CreateSignedPlugin("signed", rsa);
        var service = CreateService(enabled: true, rsa);

        var trusted = service.IsTrusted(plugin, out var error);

        Assert.True(trusted);
        Assert.Null(error);
    }

    [Fact]
    public void IsTrusted_RejectsTamperedPackageFile()
    {
        using var rsa = RSA.Create(2048);
        var plugin = CreateSignedPlugin("tampered", rsa);
        File.AppendAllText(plugin.AssemblyPath, "tampered");
        var service = CreateService(enabled: true, rsa);

        var trusted = service.IsTrusted(plugin, out var error);

        Assert.False(trusted);
        Assert.Contains("hash mismatch", error);
    }

    [Fact]
    public void IsTrusted_RejectsUnsignedExtraFile()
    {
        using var rsa = RSA.Create(2048);
        var plugin = CreateSignedPlugin("extra-file", rsa);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(plugin.AssemblyPath)!, "extra.dll"), "extra");
        var service = CreateService(enabled: true, rsa);

        var trusted = service.IsTrusted(plugin, out var error);

        Assert.False(trusted);
        Assert.Contains("unsigned file", error);
    }

    [Fact]
    public void OfficialPluginTrustKeyProvider_LoadsCommittedPublicKey()
    {
        var provider = new OfficialPluginTrustKeyProvider();

        var loaded = provider.TryGetPublicKey(
            OfficialPluginTrustKeyProvider.OfficialPluginKeyId,
            out var publicKey,
            out var error);

        using (publicKey)
        {
            Assert.True(loaded);
            Assert.Null(error);
            Assert.True(publicKey.KeySize >= 2048);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private PluginInfo CreateUnsignedPlugin(string id)
    {
        var packageDir = Path.Combine(_root, id);
        Directory.CreateDirectory(packageDir);
        var assemblyPath = Path.Combine(packageDir, "ComCross.Plugins.Test.dll");
        File.WriteAllText(assemblyPath, "test plugin");

        return CreatePluginInfo(id, assemblyPath);
    }

    private PluginInfo CreateSignedPlugin(string id, RSA privateKey)
    {
        var plugin = CreateUnsignedPlugin(id);
        var packageDir = Path.GetDirectoryName(plugin.AssemblyPath)!;
        var dependencyPath = Path.Combine(packageDir, "dependency.txt");
        File.WriteAllText(dependencyPath, "dependency");

        var signature = new PluginPackageSignature
        {
            SchemaVersion = PluginSignatureVerificationService.SupportedSchemaVersion,
            KeyId = OfficialPluginTrustKeyProvider.OfficialPluginKeyId,
            PluginId = plugin.Manifest.Id,
            Version = plugin.Manifest.Version,
            Algorithm = PluginSignatureVerificationService.SupportedAlgorithm,
            SignedAt = DateTimeOffset.Parse("2026-05-02T00:00:00Z"),
            Files =
            {
                CreateFileEntry(packageDir, plugin.AssemblyPath),
                CreateFileEntry(packageDir, dependencyPath)
            }
        };

        var payload = PluginSignatureCanonicalizer.CreatePayloadBytes(signature);
        signature.Signature = Convert.ToBase64String(privateKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss));

        File.WriteAllText(
            Path.Combine(packageDir, PluginSignatureVerificationService.SignatureFileName),
            JsonSerializer.Serialize(signature));

        return plugin;
    }

    private static PluginPackageSignatureFile CreateFileEntry(string packageDir, string filePath)
        => new()
        {
            Path = PluginSignatureCanonicalizer.NormalizeRelativePath(Path.GetRelativePath(packageDir, filePath)),
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant()
        };

    private static PluginInfo CreatePluginInfo(string id, string assemblyPath)
        => new()
        {
            AssemblyPath = assemblyPath,
            Manifest = new PluginManifest
            {
                Id = id,
                Name = "Test",
                Version = "1.0.0",
                EntryPoint = "Test.Plugin"
            }
        };

    private static PluginSignatureVerificationService CreateService(bool enabled, RSA key)
        => new(
            new PluginSignatureVerificationSettings
            {
                Enabled = enabled
            },
            new TestKeyProvider(key.ExportParameters(includePrivateParameters: false)),
            NullLogger<PluginSignatureVerificationService>.Instance);

    private sealed class TestKeyProvider : IPluginTrustKeyProvider
    {
        private readonly RSAParameters _parameters;

        public TestKeyProvider(RSAParameters parameters)
        {
            _parameters = parameters;
        }

        public bool TryGetPublicKey(string keyId, out RSA publicKey, out string? error)
        {
            publicKey = RSA.Create();
            publicKey.ImportParameters(_parameters);
            error = null;
            return true;
        }
    }
}
