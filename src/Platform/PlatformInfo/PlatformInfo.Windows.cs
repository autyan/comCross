namespace ComCross.Platform;

public static class PlatformInfo
{
    public static bool IsWindows => true;
    public static bool IsLinux => false;

    public static string PluginHostExecutableName => "ComCross.PluginHost.exe";
    public static string SessionHostExecutableName => "ComCross.SessionHost.exe";
}
