using System.Reflection;

namespace ComCross.PluginHost.Runtime;

internal sealed class PluginLifecycleManager
{
    private readonly string _entryPoint;
    private readonly string _pluginPath;

    private PluginLoadContext? _loadContext;

    public PluginLifecycleManager(string entryPoint, string pluginPath)
    {
        _entryPoint = entryPoint;
        _pluginPath = pluginPath;
    }

    public object? Instance { get; private set; }
    public string? LoadError { get; private set; }
    public bool IsLoaded => Instance != null && LoadError is null;

    public void LoadPlugin()
    {
        try
        {
            Unload();
            _loadContext = new PluginLoadContext(_pluginPath);
            var assembly = _loadContext.LoadFromAssemblyPath(Path.GetFullPath(_pluginPath));
            var type = assembly.GetType(_entryPoint, throwOnError: true);
            Instance = Activator.CreateInstance(type!);
            LoadError = null;
        }
        catch (Exception ex)
        {
            Instance = null;
            LoadError = ex.Message;
        }
    }

    public bool Restart()
    {
        LoadPlugin();
        return IsLoaded;
    }

    public void Unload()
    {
        Instance = null;
        LoadError = null;

        var ctx = _loadContext;
        _loadContext = null;
        if (ctx is null)
        {
            return;
        }

        try
        {
            ctx.Unload();
        }
        catch
        {
        }
    }
}
