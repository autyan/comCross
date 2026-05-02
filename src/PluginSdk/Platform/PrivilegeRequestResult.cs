namespace ComCross.PluginSdk.Platform;

/// <summary>
/// Result of a privileged operation request.
///
/// This is intentionally SDK-local so plugins can depend on it without referencing host/Core assemblies.
/// </summary>
public enum PrivilegeRequestResult
{
    /// <summary>
    /// Operation is already permitted.
    /// </summary>
    AlreadyGranted,

    /// <summary>
    /// Privilege was granted by an elevation flow (may be temporary).
    /// </summary>
    Granted,

    /// <summary>
    /// User denied an elevation prompt.
    /// </summary>
    Denied,

    /// <summary>
    /// This platform/runtime does not support the requested elevation mechanism.
    /// </summary>
    NotSupported,

    /// <summary>
    /// Elevation was attempted but failed.
    /// </summary>
    Failed
}
