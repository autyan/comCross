using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class AppLogLogger : ILogger
{
    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        private NoopScope() { }
        public void Dispose() { }
    }

    private readonly AppLogService _appLog;
    private readonly string _category;

    public AppLogLogger(AppLogService appLog, string category)
    {
        _appLog = appLog ?? throw new ArgumentNullException(nameof(appLog));
        _category = string.IsNullOrWhiteSpace(category) ? "Unknown" : category;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NoopScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // AppLogService internally gates output (settings-driven).
        // Keep this permissive so callers don't need to special-case.
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (formatter is null)
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is not null)
        {
            message = exception.Message;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var prefix = eventId.Id != 0
            ? $"[{_category}]({eventId.Id}) "
            : $"[{_category}] ";

        var finalMessage = prefix + message;

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
            case LogLevel.Information:
                _appLog.Info(finalMessage);
                break;
            case LogLevel.Warning:
                _appLog.Warn(finalMessage);
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                _appLog.Error(finalMessage, exception);
                break;
            default:
                _appLog.Info(finalMessage);
                break;
        }
    }
}

public sealed class AppLogLogger<T> : ILogger<T>
{
    private readonly AppLogLogger _inner;

    public AppLogLogger(AppLogService appLog)
    {
        _inner = new AppLogLogger(appLog, typeof(T).FullName ?? typeof(T).Name);
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => _inner.Log(logLevel, eventId, state, exception, formatter);
}
