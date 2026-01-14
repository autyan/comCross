namespace ComCross.Shared.Models;

public sealed class CommandDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public CommandPayloadType Type { get; set; } = CommandPayloadType.Text;
    public string Encoding { get; set; } = "UTF-8";
    public bool AppendCr { get; set; }
    public bool AppendLf { get; set; }
    public CommandScope Scope { get; set; } = CommandScope.Global;
    public string? SessionId { get; set; }
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? Hotkey { get; set; }
    public bool IsPreset { get; set; }
}

public enum CommandPayloadType
{
    Text,
    Hex
}

public enum CommandScope
{
    Global,
    Session
}
