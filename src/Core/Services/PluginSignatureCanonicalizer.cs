using System.Text.Json;

namespace ComCross.Core.Services;

public static class PluginSignatureCanonicalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static byte[] CreatePayloadBytes(PluginPackageSignature signature)
    {
        var payload = new PluginPackageSignaturePayload(
            signature.SchemaVersion,
            signature.KeyId,
            signature.PluginId,
            signature.Version,
            signature.Algorithm,
            signature.SignedAt.ToUniversalTime(),
            signature.Files
                .Select(file => new PluginPackageSignatureFilePayload(
                    NormalizeRelativePath(file.Path),
                    file.Sha256.ToLowerInvariant()))
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToList());

        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    public static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/').Trim();
}
