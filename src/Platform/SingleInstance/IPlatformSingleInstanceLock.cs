namespace ComCross.Platform.SingleInstance;

public interface IPlatformSingleInstanceLock
{
    IDisposable? TryAcquire(string instanceId, string localDataDirectory, out string? error);
}
