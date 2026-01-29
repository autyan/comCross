using System.Globalization;
using System.Resources;

namespace ComCross.PluginSdk.Resources;

internal static class PrivilegeStrings
{
    private static readonly ResourceManager ResourceManager = new(
        "ComCross.PluginSdk.Resources.PrivilegeStrings",
        typeof(PrivilegeStrings).Assembly);

    public static string Get(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }

    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }
}
