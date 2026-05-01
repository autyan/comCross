using ComCross.PluginSdk;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Bridges shared-memory pressure detection to Session Host "set-backpressure" IPC.
/// </summary>
public sealed class SharedMemoryBackpressureBridgeService : IDisposable
{
    private readonly SharedMemoryManager _sharedMemoryManager;
    private readonly PluginHostProtocolService _pluginHostProtocol;
    private readonly ILogger<SharedMemoryBackpressureBridgeService> _logger;

    public SharedMemoryBackpressureBridgeService(
        SharedMemoryManager sharedMemoryManager,
        PluginHostProtocolService pluginHostProtocol,
        ILogger<SharedMemoryBackpressureBridgeService> logger)
    {
        _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
        _pluginHostProtocol = pluginHostProtocol ?? throw new ArgumentNullException(nameof(pluginHostProtocol));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sharedMemoryManager.BackpressureDetected += OnBackpressureDetected;
    }

    private void OnBackpressureDetected(string sessionId, BackpressureLevel level)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var (ok, error) = await _pluginHostProtocol.SetBackpressureAsync(
                    sessionId,
                    level,
                    TimeSpan.FromSeconds(1));
                if (!ok)
                {
                    _logger.LogDebug(
                        "Failed to send backpressure: SessionId={SessionId}, Level={Level}, Error={Error}",
                        sessionId,
                        level,
                        error ?? "-");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send backpressure: SessionId={SessionId}, Level={Level}", sessionId, level);
            }
        });
    }

    public void Dispose()
    {
        _sharedMemoryManager.BackpressureDetected -= OnBackpressureDetected;
    }
}
