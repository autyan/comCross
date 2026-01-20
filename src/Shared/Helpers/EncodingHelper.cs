using System.Text;

namespace ComCross.Shared.Helpers;

public static class EncodingHelper
{
    public static Encoding GetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
