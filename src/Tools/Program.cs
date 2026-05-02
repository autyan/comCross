using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ComCross.Core.Models;
using ComCross.Core.Services;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

return args[0] switch
{
    "localization-test" => RunLocalizationTest(),
    "sign-plugin" => SignPlugin(args.Skip(1).ToArray()),
    _ => UnknownCommand(args[0])
};

static int SignPlugin(string[] args)
{
    var options = ParseOptions(args);
    if (!options.TryGetValue("--plugin-dir", out var pluginDir) || string.IsNullOrWhiteSpace(pluginDir))
    {
        Console.Error.WriteLine("Missing --plugin-dir.");
        return 1;
    }

    if (!options.TryGetValue("--private-key", out var privateKeyPath) || string.IsNullOrWhiteSpace(privateKeyPath))
    {
        Console.Error.WriteLine("Missing --private-key.");
        return 1;
    }

    options.TryGetValue("--key-id", out var keyId);
    keyId = string.IsNullOrWhiteSpace(keyId)
        ? OfficialPluginTrustKeyProvider.OfficialPluginKeyId
        : keyId.Trim();

    pluginDir = Path.GetFullPath(pluginDir);
    privateKeyPath = Path.GetFullPath(privateKeyPath);

    var assemblyPath = Directory
        .EnumerateFiles(pluginDir, "ComCross.Plugins.*.dll", SearchOption.TopDirectoryOnly)
        .OrderBy(path => path, StringComparer.Ordinal)
        .FirstOrDefault();

    if (assemblyPath is null)
    {
        Console.Error.WriteLine($"Plugin assembly not found under {pluginDir}.");
        return 1;
    }

    var manifest = LoadManifest(assemblyPath);
    if (manifest is null)
    {
        Console.Error.WriteLine($"Plugin manifest resource not found in {assemblyPath}.");
        return 1;
    }

    using var privateKey = RSA.Create();
    privateKey.ImportFromPem(File.ReadAllText(privateKeyPath));

    var signature = new PluginPackageSignature
    {
        SchemaVersion = PluginSignatureVerificationService.SupportedSchemaVersion,
        KeyId = keyId,
        PluginId = manifest.Id,
        Version = manifest.Version,
        Algorithm = PluginSignatureVerificationService.SupportedAlgorithm,
        SignedAt = DateTimeOffset.UtcNow
    };

    foreach (var file in Directory
                 .EnumerateFiles(pluginDir, "*", SearchOption.AllDirectories)
                 .Where(path => !string.Equals(
                     PluginSignatureCanonicalizer.NormalizeRelativePath(Path.GetRelativePath(pluginDir, path)),
                     PluginSignatureVerificationService.SignatureFileName,
                     StringComparison.Ordinal))
                 .OrderBy(path => path, StringComparer.Ordinal))
    {
        signature.Files.Add(new PluginPackageSignatureFile
        {
            Path = PluginSignatureCanonicalizer.NormalizeRelativePath(Path.GetRelativePath(pluginDir, file)),
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()
        });
    }

    var payload = PluginSignatureCanonicalizer.CreatePayloadBytes(signature);
    signature.Signature = Convert.ToBase64String(privateKey.SignData(
        payload,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pss));

    var signaturePath = Path.Combine(pluginDir, PluginSignatureVerificationService.SignatureFileName);
    File.WriteAllText(signaturePath, JsonSerializer.Serialize(signature, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Plugin signature written: {signaturePath}");
    return 0;
}

static PluginManifest? LoadManifest(string assemblyPath)
{
    var assembly = Assembly.LoadFrom(assemblyPath);
    var resourceName = assembly
        .GetManifestResourceNames()
        .FirstOrDefault(name => name.EndsWith(PluginDiscoveryService.ManifestResourceName, StringComparison.Ordinal));
    if (resourceName is null)
    {
        return null;
    }

    using var stream = assembly.GetManifestResourceStream(resourceName);
    return stream is null
        ? null
        : JsonSerializer.Deserialize<PluginManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var name = args[i];
        if (!name.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        result[name] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[++i]
            : string.Empty;
    }

    return result;
}

static int RunLocalizationTest()
{
    var assembly = Assembly.GetAssembly(typeof(ComCross.Core.Services.LocalizationService));
    Console.WriteLine("=== Embedded Resources in ComCross.Core ===");
    foreach (var name in assembly?.GetManifestResourceNames() ?? Array.Empty<string>())
    {
        Console.WriteLine(name);
    }

    var localization = new ComCross.Core.Services.LocalizationService();
    Console.WriteLine($"Current Culture: {localization.CurrentCulture}");
    Console.WriteLine($"Available Cultures: {string.Join(", ", localization.AvailableCultures.Select(c => c.Code))}");
    Console.WriteLine($"app.title: {localization.GetString("app.title")}");
    localization.SetCulture("zh-CN");
    Console.WriteLine($"app.title: {localization.GetString("app.title")}");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Usage:
          dotnet run --project src/Tools/ComCross.Tools.csproj -- localization-test
          dotnet run --project src/Tools/ComCross.Tools.csproj -- sign-plugin --plugin-dir DIR --private-key FILE [--key-id ID]
        """);
}
