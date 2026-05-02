using System.Text.Json;

namespace ComCross.Startup;

internal sealed class StartupInstanceResolver
{
    private const string ManifestFileName = "ComCross.Instance.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StartupInstanceIdentity Resolve(string installDirectory, IReadOnlyList<string> args)
    {
        var manifestPath = ResolveManifestPath(installDirectory, args);
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            return ReadManifest(manifestPath);
        }

        return StartupInstanceIdentity.Stable();
    }

    private static string? ResolveManifestPath(string installDirectory, IReadOnlyList<string> args)
    {
        var explicitPath = TryGetOption(args, "--instance-manifest");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var colocated = Path.Combine(installDirectory, ManifestFileName);
        return File.Exists(colocated) ? colocated : null;
    }

    private static StartupInstanceIdentity ReadManifest(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<StartupInstanceManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions);

        if (manifest is null)
        {
            return StartupInstanceIdentity.Stable(manifestPath);
        }

        return new StartupInstanceIdentity(
            manifest.SchemaVersion <= 0 ? 1 : manifest.SchemaVersion,
            RequireValue(manifest.Product, "ComCross"),
            RequireValue(manifest.Channel, "Stable"),
            RequireValue(manifest.DirectoryName, "ComCross"),
            RequireValue(manifest.InstanceId, "comcross-stable"),
            string.IsNullOrWhiteSpace(manifest.SchemaLine) ? "v0" : manifest.SchemaLine.Trim(),
            manifestPath);
    }

    private static string RequireValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? TryGetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.Ordinal))
            {
                continue;
            }

            return i + 1 < args.Count ? args[i + 1] : null;
        }

        return null;
    }

    private sealed record StartupInstanceManifest(
        int SchemaVersion,
        string? Product,
        string? Channel,
        string? DirectoryName,
        string? InstanceId,
        string? SchemaLine);
}

internal sealed record StartupInstanceIdentity(
    int SchemaVersion,
    string Product,
    string Channel,
    string DirectoryName,
    string InstanceId,
    string SchemaLine,
    string? ManifestPath)
{
    public static StartupInstanceIdentity Stable(string? manifestPath = null)
        => new(1, "ComCross", "Stable", "ComCross", "comcross-stable", "v0", manifestPath);
}
