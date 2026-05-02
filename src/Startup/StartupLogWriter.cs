namespace ComCross.Startup;

internal sealed class StartupLogWriter
{
    private readonly string _logPath;

    public StartupLogWriter(string logDirectory)
    {
        LogDirectory = logDirectory;
        _logPath = Path.Combine(logDirectory, "startup.log");
    }

    public string LogDirectory { get; }

    public async Task WriteLineAsync(string message)
    {
        Directory.CreateDirectory(LogDirectory);
        var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(_logPath, line);
    }
}
