using ComCross.Shared.Models;

namespace ComCross.Core.Services;

internal static class CommandDefaultCatalog
{
    public static void EnsureDefaults(CommandSettings settings, string groupName)
    {
        if (settings.DefaultsInitialized)
        {
            return;
        }

        if (settings.GlobalCommands.Count == 0 && settings.SessionCommands.Count == 0)
        {
            settings.GlobalCommands.AddRange(CreateDefaults(groupName));
        }

        settings.DefaultsInitialized = true;
    }

    private static IEnumerable<CommandDefinition> CreateDefaults(string groupName)
    {
        var order = 0;

        yield return Text("Ping", "ping", groupName, ++order);
        yield return Text("Hello", "hello", groupName, ++order);
        yield return Text("Status?", "status?", groupName, ++order);
        yield return Text("Version?", "version?", groupName, ++order);

        yield return Text("AT", "AT", groupName, ++order, appendCr: true, isPinned: true);
        yield return Text("ATI", "ATI", groupName, ++order, appendCr: true);
        yield return Text("AT+GMR", "AT+GMR", groupName, ++order, appendCr: true);
        yield return Text("AT+RST", "AT+RST", groupName, ++order, appendCr: true);

        yield return Text("CR", string.Empty, groupName, ++order, appendCr: true);
        yield return Text("LF", string.Empty, groupName, ++order, appendLf: true);
        yield return Text("CRLF", string.Empty, groupName, ++order, appendCr: true, appendLf: true, isPinned: true);

        yield return Hex("00", "00", groupName, ++order);
        yield return Hex("FF", "FF", groupName, ++order);
        yield return Hex("01 03", "01 03", groupName, ++order);
        yield return Hex("Heartbeat", "AA 55", groupName, ++order, isPinned: true);

        yield return Text("GET /", "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n", groupName, ++order);
        yield return Text("JSON Ping", """{"type":"ping"}""", groupName, ++order, appendLf: true);
    }

    private static CommandDefinition Text(
        string name,
        string payload,
        string group,
        int order,
        bool appendCr = false,
        bool appendLf = false,
        bool isPinned = false)
        => new()
        {
            Id = CreateId(name),
            Name = name,
            Payload = payload,
            Group = group,
            SortOrder = order,
            AppendCr = appendCr,
            AppendLf = appendLf,
            Type = CommandPayloadType.Text,
            Scope = CommandScope.Global,
            Encoding = "UTF-8",
            IsPreset = true,
            IsPinned = isPinned
        };

    private static CommandDefinition Hex(
        string name,
        string payload,
        string group,
        int order,
        bool isPinned = false)
        => new()
        {
            Id = CreateId(name),
            Name = name,
            Payload = payload,
            Group = group,
            SortOrder = order,
            Type = CommandPayloadType.Hex,
            Scope = CommandScope.Global,
            Encoding = "UTF-8",
            IsPreset = true,
            IsPinned = isPinned
        };

    private static string CreateId(string name)
        => "default-" + name
            .ToLowerInvariant()
            .Replace("+", "plus", StringComparison.Ordinal)
            .Replace("/", "slash", StringComparison.Ordinal)
            .Replace("?", "q", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);
}
