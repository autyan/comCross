using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace ComCross.Core.Services;

public sealed class PluginDiscoveryService
{
    public const string ManifestResourceName = "ComCross.Plugin.Manifest.json";

    public IReadOnlyList<PluginInfo> Discover(string pluginsDirectory)
    {
        if (!Directory.Exists(pluginsDirectory))
        {
            return Array.Empty<PluginInfo>();
        }

        var plugins = new List<PluginInfo>();
        var dllFiles = Directory.EnumerateFiles(pluginsDirectory, "*.dll", SearchOption.AllDirectories);

        foreach (var dll in dllFiles)
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

        return plugins;
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
