using System.Collections.Concurrent;

namespace ComCross.Core.Services;

public static class PluginManagerPlaneState
{
    public static void ReplacePlane(
        ConcurrentDictionary<string, PluginRuntime> knownRuntimes,
        ConcurrentDictionary<string, PluginRuntime> activeRuntimes,
        PluginPlane plane,
        IReadOnlyList<PluginRuntime> replacements)
    {
        ArgumentNullException.ThrowIfNull(knownRuntimes);
        ArgumentNullException.ThrowIfNull(activeRuntimes);
        ArgumentNullException.ThrowIfNull(replacements);

        foreach (var runtime in knownRuntimes.ToArray())
        {
            if (TryGetPlane(runtime.Value.Info.Manifest, out var currentPlane) && currentPlane == plane)
            {
                knownRuntimes.TryRemove(runtime.Key, out _);
            }
        }

        foreach (var runtime in activeRuntimes.ToArray())
        {
            if (TryGetPlane(runtime.Value.Info.Manifest, out var currentPlane) && currentPlane == plane)
            {
                activeRuntimes.TryRemove(runtime.Key, out _);
            }
        }

        foreach (var runtime in replacements)
        {
            knownRuntimes[runtime.Info.Manifest.Id] = runtime;
            if (runtime.State == PluginLoadState.Loaded)
            {
                activeRuntimes[runtime.Info.Manifest.Id] = runtime;
            }
        }
    }

    public static bool TryGetPlane(PluginManifest manifest, out PluginPlane plane)
        => PluginPlaneClassifier.TryClassify(manifest, out plane, out _);
}
