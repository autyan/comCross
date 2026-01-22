namespace ComCross.Platform;

public static class PlatformInfo
{
    public static bool IsWindows => false;
    public static bool IsLinux => true;

    public static string PluginHostExecutableName => "ComCross.PluginHost";
    public static string SessionHostExecutableName => "ComCross.SessionHost";
}
