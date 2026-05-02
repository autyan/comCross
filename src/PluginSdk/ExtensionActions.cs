namespace ComCross.PluginSdk;

/// <summary>
/// White-listed action names that extension-plane plugins may request from Core.
/// </summary>
public static class ExtensionActionNames
{
    public const string SendDataToSession = "send-data-to-session";
    public const string PublishNotification = "publish-notification";
}

/// <summary>
/// Plugin-raised action request that is emitted by ExtensionHost back to Core.
/// </summary>
public sealed record ExtensionActionRequest(
    string Action,
    string? SessionId = null,
    object? Payload = null)
{
    public static ExtensionActionRequest SendDataToSession(string sessionId, byte[] data, string? format = null)
        => new(
            ExtensionActionNames.SendDataToSession,
            sessionId,
            new ExtensionSendDataRequest(sessionId, data, format));

    public static ExtensionActionRequest PublishNotification(
        string messageKey,
        object[]? messageArgs = null,
        string? category = null,
        string? level = null)
        => new(
            ExtensionActionNames.PublishNotification,
            null,
            new ExtensionNotificationRequest(messageKey, messageArgs, category, level));
}

/// <summary>
/// Optional event source for extension plugins that need Core to execute white-listed actions.
/// </summary>
public interface IExtensionActionRequestSource
{
    event EventHandler<ExtensionActionRequest>? ActionRequested;
}

/// <summary>
/// Request to send bytes to an already existing session.
/// </summary>
public sealed record ExtensionSendDataRequest(
    string SessionId,
    byte[] Data,
    string? Format = null);

/// <summary>
/// Request to publish a localized notification through Core's notification center.
/// MessageKey must be prefixed by the plugin id.
/// </summary>
public sealed record ExtensionNotificationRequest(
    string MessageKey,
    object[]? MessageArgs = null,
    string? Category = null,
    string? Level = null);
