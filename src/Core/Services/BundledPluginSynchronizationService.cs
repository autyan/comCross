using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class BundledPluginSynchronizationService
{
    private readonly ComCrossPathService _paths;
    private readonly ILogger<BundledPluginSynchronizationService> _logger;

    public BundledPluginSynchronizationService(
        ComCrossPathService paths,
        ILogger<BundledPluginSynchronizationService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Synchronize()
    {
        var sourceRoot = _paths.BundledPluginsDirectory;
        var runtimeRoot = _paths.RuntimePluginsDirectory;

        Directory.CreateDirectory(runtimeRoot);

        if (!Directory.Exists(sourceRoot))
        {
            _logger.LogInformation("Bundled plugin seed directory not found: {BundledPluginsDirectory}", sourceRoot);
            return;
        }

        var copied = 0;
        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var folderName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var targetDirectory = Path.Combine(runtimeRoot, folderName);
            if (PathsEqual(sourceDirectory, targetDirectory))
            {
                continue;
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            CopyDirectory(sourceDirectory, targetDirectory);
            copied++;
        }

        _logger.LogInformation(
            "Synchronized {Count} bundled plugin package(s) from {SourceDirectory} to {RuntimeDirectory}",
            copied,
            sourceRoot,
            runtimeRoot);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: true);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var targetChild = Path.Combine(targetDirectory, Path.GetFileName(childDirectory));
            CopyDirectory(childDirectory, targetChild);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }
}
