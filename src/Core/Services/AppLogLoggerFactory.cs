using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class AppLogLoggerFactory : ILoggerFactory
{
    private readonly AppLogService _appLog;

    public AppLogLoggerFactory(AppLogService appLog)
    {
        _appLog = appLog ?? throw new ArgumentNullException(nameof(appLog));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new AppLogLogger(_appLog, categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        // Not supported: AppLogService is the only sink.
    }

    public void Dispose()
    {
    }
}
