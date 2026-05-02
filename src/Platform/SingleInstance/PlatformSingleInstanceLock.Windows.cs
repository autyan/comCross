using System.Threading;

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

        var name = $@"Local\ComCross.{Sanitize(instanceId)}";
        try
        {
            var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (createdNew)
            {
                error = null;
                return mutex;
            }

            mutex.Dispose();
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
