using System.Text.Json;

namespace ComCross.Core.Application;

public static class ComCrossInstanceResolver
{
    private const string ManifestFileName = "ComCross.Instance.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ComCrossInstanceIdentity Resolve(string installDirectory, IReadOnlyList<string>? args = null)
    {
        var manifestPath = ResolveManifestPath(installDirectory, args);
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            return ReadManifest(manifestPath);
        }

        return ComCrossInstanceIdentity.Stable();
    }

    private static string? ResolveManifestPath(string installDirectory, IReadOnlyList<string>? args)
    {
        var explicitPath = TryGetOption(args, "--instance-manifest");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var colocated = Path.Combine(installDirectory, ManifestFileName);
        return File.Exists(colocated) ? colocated : null;
    }

    private static ComCrossInstanceIdentity ReadManifest(string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ComCrossInstanceManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions);

        if (manifest is null)
        {
            throw new InvalidDataException("Invalid ComCross instance manifest.");
        }

        return new ComCrossInstanceIdentity(
            manifest.SchemaVersion <= 0 ? 1 : manifest.SchemaVersion,
            RequireValue(manifest.Product, nameof(manifest.Product)),
            RequireValue(manifest.Channel, nameof(manifest.Channel)),
            RequireValue(manifest.DirectoryName, nameof(manifest.DirectoryName)),
            RequireValue(manifest.InstanceId, nameof(manifest.InstanceId)),
            string.IsNullOrWhiteSpace(manifest.SchemaLine) ? "v0" : manifest.SchemaLine.Trim(),
            manifestPath);
    }

    private static string RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"ComCross instance manifest is missing {name}.");
        }

        return value.Trim();
    }

    private static string? TryGetOption(IReadOnlyList<string>? args, string optionName)
    {
        if (args is null)
        {
            return null;
        }

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

    private sealed record ComCrossInstanceManifest(
        int SchemaVersion,
        string? Product,
        string? Channel,
        string? DirectoryName,
        string? InstanceId,
        string? SchemaLine);
}
