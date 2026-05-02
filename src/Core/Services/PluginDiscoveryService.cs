using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace ComCross.Core.Services;

public sealed class PluginDiscoveryService
{
    public const string ManifestResourceName = "ComCross.Plugin.Manifest.json";

    private static readonly Version ZeroVersion = new(0, 0, 0, 0);

    public IReadOnlyList<PluginInfo> Discover(string pluginsDirectory)
    {
        if (!Directory.Exists(pluginsDirectory))
        {
            return Array.Empty<PluginInfo>();
        }

        var plugins = new List<PluginInfo>();

        // Preferred layout: each plugin is a directory under plugins/.
        // Example:
        //   plugins/ComCross.Plugins.Serial/ComCross.Plugins.Serial.dll
        //   plugins/ComCross.Plugins.Serial/System.IO.Ports.dll
        // This enables plugin-local dependencies and native assets.
        var pluginDirs = Directory.EnumerateDirectories(pluginsDirectory, "*", SearchOption.TopDirectoryOnly);
        var discoveredFromDirs = false;

        foreach (var dir in pluginDirs)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(dir, "ComCross.Plugins.*.dll", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var dll in candidates)
            {
                var manifest = LoadManifest(dll);
                if (manifest == null)
                {
                    continue;
                }

                plugins.Add(new PluginInfo
                {
                    AssemblyPath = dll,
                    Manifest = manifest
                });
                discoveredFromDirs = true;
            }
        }

        if (discoveredFromDirs)
        {
            return Deduplicate(plugins);
        }

        // Legacy layout: plugin DLLs copied directly into plugins/.
        IEnumerable<string> flatCandidates;
        try
        {
            flatCandidates = Directory.EnumerateFiles(pluginsDirectory, "ComCross.Plugins.*.dll", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return plugins;
        }

        foreach (var dll in flatCandidates)
        {
            var manifest = LoadManifest(dll);
            if (manifest == null)
            {
                continue;
            }

            plugins.Add(new PluginInfo
            {
                AssemblyPath = dll,
                Manifest = manifest
            });
        }

        return Deduplicate(plugins);
    }

    private static IReadOnlyList<PluginInfo> Deduplicate(List<PluginInfo> plugins)
    {
        if (plugins.Count <= 1)
        {
            return plugins;
        }

        // Upgrade-friendly: allow multiple plugin packages on disk, but load only the best candidate per plugin id.
        // Rule: higher Manifest.Version wins; ties broken by lexicographic AssemblyPath.
        var bestById = new Dictionary<string, PluginInfo>(StringComparer.Ordinal);

        foreach (var plugin in plugins)
        {
            var id = plugin.Manifest.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!bestById.TryGetValue(id, out var current))
            {
                bestById[id] = plugin;
                continue;
            }

            var currentVersion = ParseVersionSafe(current.Manifest.Version);
            var candidateVersion = ParseVersionSafe(plugin.Manifest.Version);

            if (candidateVersion > currentVersion)
            {
                bestById[id] = plugin;
                continue;
            }

            if (candidateVersion == currentVersion
                && string.Compare(plugin.AssemblyPath, current.AssemblyPath, StringComparison.Ordinal) > 0)
            {
                bestById[id] = plugin;
            }
        }

        return bestById.Values.ToList();
    }

    private static Version ParseVersionSafe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ZeroVersion;
        }

        // Be permissive: accept "1.2.3". Ignore suffixes like "-alpha" by taking the numeric prefix.
        var core = text.Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? v : ZeroVersion;
    }

    private static PluginManifest? LoadManifest(string dllPath)
    {
        try
        {
            var assembly = LoadPluginAssemblyForDiscovery(dllPath);
            var resourceName = FindManifestResourceName(assembly);
            if (resourceName is null)
            {
                return null;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PluginManifest>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static Assembly LoadPluginAssemblyForDiscovery(string dllPath)
    {
        // Discovery needs to read an embedded resource, but plugin DLLs may depend on assemblies
        // that are not copied into the plugins directory (e.g., ComCross.PluginSdk.dll lives in
        // AppContext.BaseDirectory). Loading via Assembly.LoadFrom can fail dependency resolution.
        // Use an isolated load context with a resolver probing both base dir and plugin dir.

        var fullPath = Path.GetFullPath(dllPath);
        var pluginDir = Path.GetDirectoryName(fullPath);
        var baseDir = AppContext.BaseDirectory;

        var alc = new AssemblyLoadContext($"ComCross-PluginDiscovery-{Guid.NewGuid():N}", isCollectible: true);
        alc.Resolving += (_, name) =>
        {
            var simpleName = name.Name;
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            // Probe base directory first (shared deps live here).
            var candidate = Path.Combine(baseDir, simpleName + ".dll");
            if (File.Exists(candidate))
            {
                return alc.LoadFromAssemblyPath(candidate);
            }

            // Probe plugin directory next (plugin-local deps).
            if (!string.IsNullOrWhiteSpace(pluginDir))
            {
                candidate = Path.Combine(pluginDir, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    return alc.LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        };

        return alc.LoadFromAssemblyPath(fullPath);
    }

    private static string? FindManifestResourceName(Assembly assembly)
    {
        // Plugins typically embed the manifest as:
        //   <EmbeddedResource Include="Resources\\ComCross.Plugin.Manifest.json" />
        // which becomes something like:
        //   "ComCross.Plugins.Serial.Resources.ComCross.Plugin.Manifest.json"
        // So we match by suffix to avoid hardcoding the namespace/path.
        try
        {
            return assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(ManifestResourceName, StringComparison.Ordinal));
        }
        catch
        {
            return null;
        }
    }
}
