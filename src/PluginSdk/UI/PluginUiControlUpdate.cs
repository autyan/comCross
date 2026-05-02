namespace ComCross.PluginSdk.UI;

public enum PluginUiControlUpdateKind
{
    Value = 0,
    Options = 1,
    Programmatic = 2,
}

public readonly record struct PluginUiControlUpdate(
    string Key,
    PluginUiControlUpdateKind Kind,
    object? Value);
