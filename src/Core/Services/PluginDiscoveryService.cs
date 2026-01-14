using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            var assembly = Assembly.LoadFrom(dllPath);
            using var stream = assembly.GetManifestResourceStream(ManifestResourceName);
            if (stream == null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PluginManifest>(stream);
        }
        catch
        {
            return null;
        }
    }
}
