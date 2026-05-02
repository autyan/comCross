using System.Reflection;
using System.Security.Cryptography;

namespace ComCross.Core.Services;

public interface IPluginTrustKeyProvider
{
    bool TryGetPublicKey(string keyId, out RSA publicKey, out string? error);
}

public sealed class OfficialPluginTrustKeyProvider : IPluginTrustKeyProvider
{
    public const string OfficialPluginKeyId = "comcross-plugin-official-2026";

    private const string OfficialPluginKeyResourceName = "ComCross.Security.Keys.comcross-plugin-official-2026.pub.pem";

    public bool TryGetPublicKey(string keyId, out RSA publicKey, out string? error)
    {
        publicKey = null!;

        if (!string.Equals(keyId, OfficialPluginKeyId, StringComparison.Ordinal))
        {
            error = $"Unknown plugin signing key id: {keyId}";
            return false;
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(OfficialPluginKeyResourceName);
        if (stream is null)
        {
            error = $"Trusted plugin signing key resource is missing: {OfficialPluginKeyResourceName}";
            return false;
        }

        using var reader = new StreamReader(stream);
        var pem = reader.ReadToEnd();
        publicKey = RSA.Create();

        try
        {
            publicKey.ImportFromPem(pem);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            publicKey.Dispose();
            publicKey = null!;
            error = ex.Message;
            return false;
        }
    }
}
