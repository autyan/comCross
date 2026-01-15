namespace ComCross.Shared.Models;

/// <summary>
/// Exception thrown when serial port access is denied due to insufficient permissions
/// </summary>
public sealed class SerialPortAccessDeniedException : UnauthorizedAccessException
{
    public string PortPath { get; }

    public SerialPortAccessDeniedException(string portPath)
        : base($"Access denied to serial port: {portPath}")
    {
        PortPath = portPath;
    }

    public SerialPortAccessDeniedException(string portPath, string message)
        : base(message)
    {
        PortPath = portPath;
    }

    public SerialPortAccessDeniedException(string portPath, string message, Exception innerException)
        : base(message, innerException)
    {
        PortPath = portPath;
    }
}
