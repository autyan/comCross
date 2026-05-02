namespace ComCross.PluginSdk.UI;

/// <summary>
/// Identifies a UI surface and (optionally) a particular instance of that surface.
///
/// - ViewKind: stable semantic key (e.g. "sidebar-config", "connect-dialog", "settings").
/// - ViewInstanceId: optional per-open instance id (e.g. GUID) to avoid sharing draft state or controls.
///
/// Notes:
/// - Renderer cache may include ViewInstanceId.
/// - Persistence should typically use only ViewKind.
/// </summary>
public readonly record struct PluginUiViewScope(string ViewKind, string? ViewInstanceId = null)
{
    public string ScopeKey
        => string.IsNullOrWhiteSpace(ViewInstanceId)
            ? ViewKind
            : $"{ViewKind}::{ViewInstanceId}";

    public static PluginUiViewScope From(string? viewKind, string? viewInstanceId = null)
        => new(string.IsNullOrWhiteSpace(viewKind) ? "default" : viewKind, viewInstanceId);
}
