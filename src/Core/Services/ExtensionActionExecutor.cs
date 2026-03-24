using System.Text.Json;
using ComCross.PluginSdk;
using ComCross.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

/// <summary>
/// Executes the white-listed extension-plane actions that are allowed to cross back into Core.
/// </summary>
public sealed class ExtensionActionExecutor : IExtensionActionExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IServiceProvider _services;
    private readonly ILogger<ExtensionActionExecutor> _logger;

    public ExtensionActionExecutor(
        IServiceProvider services,
        ILogger<ExtensionActionExecutor> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(
        PluginRuntime runtime,
        PluginHostExtensionActionRequestEvent request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(request);

        var pluginId = string.IsNullOrWhiteSpace(request.PluginId)
            ? runtime.Info.Manifest.Id
            : request.PluginId;

        switch (request.Action)
        {
            case ExtensionActionNames.SendDataToSession:
                await ExecuteSendDataAsync(pluginId, request, cancellationToken);
                return;

            case ExtensionActionNames.PublishNotification:
                await ExecutePublishNotificationAsync(pluginId, request, cancellationToken);
                return;

            default:
                _logger.LogWarning(
                    "Ignored unsupported extension action request: PluginId={PluginId}, Action={Action}",
                    pluginId,
                    request.Action);
                return;
        }
    }

    private async Task ExecuteSendDataAsync(
        string pluginId,
        PluginHostExtensionActionRequestEvent request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is null)
        {
            _logger.LogWarning(
                "Ignored extension send-data request with missing payload: PluginId={PluginId}",
                pluginId);
            return;
        }

        ExtensionSendDataRequest? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<ExtensionSendDataRequest>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ignored extension send-data request with invalid payload: PluginId={PluginId}",
                pluginId);
            return;
        }

        var sessionId = !string.IsNullOrWhiteSpace(payload?.SessionId)
            ? payload!.SessionId
            : request.SessionId;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            _logger.LogWarning(
                "Ignored extension send-data request without session id: PluginId={PluginId}",
                pluginId);
            return;
        }

        var workspaceCoordinator = _services.GetRequiredService<IWorkspaceCoordinator>();
        await workspaceCoordinator.SendDataAsync(sessionId, payload?.Data ?? Array.Empty<byte>());
    }

    private async Task ExecutePublishNotificationAsync(
        string pluginId,
        PluginHostExtensionActionRequestEvent request,
        CancellationToken cancellationToken)
    {
        if (request.Payload is null)
        {
            _logger.LogWarning(
                "Ignored extension publish-notification request with missing payload: PluginId={PluginId}",
                pluginId);
            return;
        }

        ExtensionNotificationRequest? payload;
        try
        {
            payload = request.Payload.Value.Deserialize<ExtensionNotificationRequest>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ignored extension publish-notification request with invalid payload: PluginId={PluginId}",
                pluginId);
            return;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.MessageKey))
        {
            _logger.LogWarning(
                "Ignored extension publish-notification request with empty message key: PluginId={PluginId}",
                pluginId);
            return;
        }

        var requiredPrefix = pluginId + ".";
        if (!payload.MessageKey.StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Ignored extension notification with invalid key prefix: PluginId={PluginId}, MessageKey={MessageKey}",
                pluginId,
                payload.MessageKey);
            return;
        }

        var category = ParseEnum(payload.Category, NotificationCategory.System);
        var level = ParseEnum(payload.Level, NotificationLevel.Info);

        var notificationService = _services.GetRequiredService<NotificationService>();
        await notificationService.AddAsync(
            category,
            level,
            payload.MessageKey,
            payload.MessageArgs ?? Array.Empty<object>(),
            cancellationToken);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
