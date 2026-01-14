namespace ComCross.Shared.Models;

public sealed class AppException : Exception
{
    public AppException(string message, AppErrorCategory category, Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
    }

    public AppErrorCategory Category { get; }
}

public enum AppErrorCategory
{
    Unknown,
    Configuration,
    Storage,
    Device,
    Plugin,
    Export,
    Localization
}
