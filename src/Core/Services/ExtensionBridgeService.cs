using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ComCross.PluginSdk;
using ComCross.Shared.Events;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Services;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class ExtensionBridgeService : IDisposable
{
    private const int MaxFramesPerBatch = 256;

    private readonly ExtensionRuntimeService _extensionRuntimeService;
    private readonly DeviceService _deviceService;
    private readonly WorkloadService _workloadService;
    private readonly SettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly IFrameStore _frameStore;
    private readonly ILogger<ExtensionBridgeService> _logger;
    private readonly Channel<BridgeWorkItem> _work = Channel.CreateUnbounded<BridgeWorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, byte> _pendingSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _frameCursors = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly IDisposable _sessionCreatedSubscription;
    private readonly IDisposable _sessionClosedSubscription;
    private readonly IDisposable _sessionRenamedSubscription;
    private readonly IDisposable _workloadCreatedSubscription;
    private readonly IDisposable _workloadDeletedSubscription;
    private readonly IDisposable _workloadRenamedSubscription;
    private readonly IDisposable _workloadSessionMembershipChangedSubscription;
    private readonly IDisposable _activeWorkloadChangedSubscription;
    private Task? _loop;
    private int _started;

    public ExtensionBridgeService(
        ExtensionRuntimeService extensionRuntimeService,
        DeviceService deviceService,
        WorkloadService workloadService,
        SettingsService settingsService,
        ILocalizationService localization,
        IFrameStore frameStore,
        IEventBus eventBus,
        ILogger<ExtensionBridgeService> logger)
    {
        _extensionRuntimeService = extensionRuntimeService ?? throw new ArgumentNullException(nameof(extensionRuntimeService));
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _workloadService = workloadService ?? throw new ArgumentNullException(nameof(workloadService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _frameStore = frameStore ?? throw new ArgumentNullException(nameof(frameStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _frameStore.FramesAppended += OnFramesAppended;
        _settingsService.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;

        _sessionCreatedSubscription = eventBus.Subscribe<SessionCreatedEvent>(_ => EnqueueContextSync());
        _sessionClosedSubscription = eventBus.Subscribe<SessionClosedEvent>(_ => EnqueueContextSync());
        _sessionRenamedSubscription = eventBus.Subscribe<SessionRenamedEvent>(_ => EnqueueContextSync());
        _workloadCreatedSubscription = eventBus.Subscribe<WorkloadCreatedEvent>(_ => EnqueueContextSync());
        _workloadDeletedSubscription = eventBus.Subscribe<WorkloadDeletedEvent>(_ => EnqueueContextSync());
        _workloadRenamedSubscription = eventBus.Subscribe<WorkloadRenamedEvent>(_ => EnqueueContextSync());
        _workloadSessionMembershipChangedSubscription = eventBus.Subscribe<WorkloadSessionMembershipChangedEvent>(_ => EnqueueContextSync());
        _activeWorkloadChangedSubscription = eventBus.Subscribe<ActiveWorkloadChangedEvent>(_ => EnqueueContextSync());

        Start();
        EnqueueContextSync();
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _loop = Task.Run(LoopAsync);
        _logger.LogInformation("ExtensionBridgeService started.");
    }

    private void OnFramesAppended(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (_pendingSessions.TryAdd(sessionId, 0))
        {
            _work.Writer.TryWrite(new BridgeWorkItem(BridgeWorkItemKind.FrameBatch, sessionId));
        }
    }

    private void OnSettingsChanged(object? sender, Shared.Models.AppSettings settings)
        => EnqueueContextSync();

    private void OnLanguageChanged(object? sender, string cultureCode)
        => EnqueueContextSync();

    private void EnqueueContextSync()
    {
        _work.Writer.TryWrite(new BridgeWorkItem(BridgeWorkItemKind.ContextSync, null));
    }

    private async Task LoopAsync()
    {
        try
        {
            await foreach (var item in _work.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    if (item.Kind == BridgeWorkItemKind.ContextSync)
                    {
                        await SyncContextAsync(_cts.Token);
                    }
                    else if (!string.IsNullOrWhiteSpace(item.SessionId))
                    {
                        await PumpFramesAsync(item.SessionId, _cts.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Extension bridge work item failed: {Kind}", item.Kind);
                }
                finally
                {
                    if (item.Kind == BridgeWorkItemKind.FrameBatch && !string.IsNullOrWhiteSpace(item.SessionId))
                    {
                        _pendingSessions.TryRemove(item.SessionId, out _);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncContextAsync(CancellationToken cancellationToken)
    {
        var sessions = _deviceService.GetAllSessions()
            .Select(session => new ExtensionSessionSnapshot(
                session.Id,
                session.Name,
                session.AdapterId,
                session.PluginId,
                session.CapabilityId,
                session.Status.ToString(),
                session.ParentSessionId,
                session.DisplayTitle,
                session.DisplaySubtitle,
                session.DisplayIcon,
                session.CanReconnect,
                session.ManagedResourceKinds,
                session.ParametersJson,
                session.RxBytes,
                session.TxBytes))
            .ToList();

        var workloads = (await _workloadService.GetAllWorkloadsAsync())
            .Select(workload => new ExtensionWorkloadSnapshot(
                workload.Id,
                workload.Name,
                workload.IsDefault,
                workload.Description,
                workload.SessionIds.ToList()))
            .ToList();

        var snapshot = new ExtensionContextSnapshot(
            Sessions: sessions,
            Workloads: workloads,
            ActiveWorkloadId: await _workloadService.GetActiveWorkloadIdAsync(),
            Language: _localization.CurrentCulture,
            Settings: JsonSerializer.SerializeToElement(_settingsService.Current));

        await _extensionRuntimeService.PushContextAsync(snapshot, cancellationToken);
    }

    private async Task PumpFramesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var cursor = _frameCursors.GetOrAdd(sessionId, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            var frames = _frameStore.ReadAfter(sessionId, cursor, MaxFramesPerBatch, out var firstAvailableFrameId);
            if (frames.Count == 0)
            {
                return;
            }

            if (cursor + 1 < firstAvailableFrameId)
            {
                cursor = firstAvailableFrameId - 1;
            }

            var batch = frames
                .Select(frame => new ExtensionFrame(
                    frame.FrameId,
                    frame.SessionId,
                    frame.TimestampUtc,
                    frame.Direction.ToString(),
                    frame.RawData,
                    frame.Format.ToString(),
                    frame.Source))
                .ToList();

            await _extensionRuntimeService.PushFrameBatchAsync(batch, cancellationToken);

            cursor = frames[^1].FrameId;
            _frameCursors[sessionId] = cursor;

            if (frames.Count < MaxFramesPerBatch)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _frameStore.FramesAppended -= OnFramesAppended;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;

            _sessionCreatedSubscription.Dispose();
            _sessionClosedSubscription.Dispose();
            _sessionRenamedSubscription.Dispose();
        _workloadCreatedSubscription.Dispose();
            _workloadDeletedSubscription.Dispose();
            _workloadRenamedSubscription.Dispose();
            _workloadSessionMembershipChangedSubscription.Dispose();
            _activeWorkloadChangedSubscription.Dispose();

        try { _cts.Cancel(); } catch { }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

    private sealed record BridgeWorkItem(BridgeWorkItemKind Kind, string? SessionId);

    private enum BridgeWorkItemKind
    {
        ContextSync,
        FrameBatch
    }
}
