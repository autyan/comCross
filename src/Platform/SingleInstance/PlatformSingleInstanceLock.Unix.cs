namespace ComCross.Platform.SingleInstance;

public sealed class PlatformSingleInstanceLock : IPlatformSingleInstanceLock
{
    public IDisposable? TryAcquire(string instanceId, string localDataDirectory, out string? error)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            error = "Missing instance id.";
            return null;
        }

        try
        {
            Directory.CreateDirectory(localDataDirectory);
            var path = Path.Combine(localDataDirectory, $".{Sanitize(instanceId)}.lock");
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.Flush();
            stream.Flush(flushToDisk: true);

            error = null;
            return stream;
        }
        catch (IOException)
        {
            error = "ComCross is already running for this instance.";
            return null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        return string.Concat(chars);
    }
}
