using ComCross.Shared.Models;

namespace ComCross.Shell.Models;

public enum ConnectionEditorMode
{
    Create,
    Reconnect,
    SessionContext
}

public sealed record ConnectionEditorContext(
    ConnectionEditorMode Mode,
    Session? Session)
{
    public static ConnectionEditorContext Create()
        => new(ConnectionEditorMode.Create, Session: null);

    public static ConnectionEditorContext Reconnect(Session session)
        => new(ConnectionEditorMode.Reconnect, session);

    public static ConnectionEditorContext SessionContext(Session session)
        => new(ConnectionEditorMode.SessionContext, session);

    public string? SessionId => Session?.Id;

    public string? StateSessionId
        => Mode is ConnectionEditorMode.Reconnect or ConnectionEditorMode.SessionContext
            ? Session?.Id
            : null;

    public string? ReconnectSessionId
        => Mode == ConnectionEditorMode.Reconnect ? Session?.Id : null;
}
