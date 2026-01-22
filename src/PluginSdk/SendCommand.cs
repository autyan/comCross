namespace ComCross.PluginSdk;

public sealed record PluginSendCommand(
    string SessionId,
    byte[] Data);
