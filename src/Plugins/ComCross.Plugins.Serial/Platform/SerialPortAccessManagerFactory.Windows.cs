namespace ComCross.Plugins.Serial.Platform;

public static class SerialPortAccessManagerFactory
{
    public static ISerialPortAccessManager CreateDefault() => new DefaultSerialPortAccessManager();
}
