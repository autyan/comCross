using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Bridges shared-memory pressure detection to Session Host "set-backpressure" IPC.
/// </summary>
public sealed class SharedMemoryBackpressureBridgeService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SharedMemoryManager _sharedMemoryManager;
    private readonly SessionHostRuntimeService _sessionHostRuntimeService;
    private readonly ILogger<SharedMemoryBackpressureBridgeService> _logger;

    public SharedMemoryBackpressureBridgeService(
        SharedMemoryManager sharedMemoryManager,
        SessionHostRuntimeService sessionHostRuntimeService,
        ILogger<SharedMemoryBackpressureBridgeService> logger)
    {
        _sharedMemoryManager = sharedMemoryManager ?? throw new ArgumentNullException(nameof(sharedMemoryManager));
        _sessionHostRuntimeService = sessionHostRuntimeService ?? throw new ArgumentNullException(nameof(sessionHostRuntimeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sharedMemoryManager.BackpressureDetected += OnBackpressureDetected;
    }

    private void OnBackpressureDetected(string sessionId, BackpressureLevel level)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var sessionHost = _sessionHostRuntimeService.TryGet(sessionId);
        if (sessionHost is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = JsonSerializer.SerializeToElement(
                    new PluginHostSetBackpressurePayload(sessionId, level),
                    JsonOptions);

                await sessionHost.Client.SendAsync(
                    new PluginHostRequest(Guid.NewGuid().ToString("N"), PluginHostMessageTypes.SetBackpressure, SessionId: sessionId, Payload: payload),
                    TimeSpan.FromSeconds(1));
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
