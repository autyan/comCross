namespace ComCross.Shared.Models;

/// <summary>
/// Serial port configuration settings
/// </summary>
public sealed class SerialSettings
{
    public int BaudRate { get; set; } = 115200;
    public int DataBits { get; set; } = 8;
    public Parity Parity { get; set; } = Parity.None;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake FlowControl { get; set; } = Handshake.None;
    public string Encoding { get; set; } = "UTF-8";
}

public enum Parity
{
    None,
    Odd,
    Even,
    Mark,
    Space
}

public enum StopBits
{
    None = 0,
    One = 1,
    Two = 2,
    OnePointFive = 3
}

public enum Handshake
{
    None,
    XOnXOff,
    RequestToSend,
    RequestToSendXOnXOff
}
