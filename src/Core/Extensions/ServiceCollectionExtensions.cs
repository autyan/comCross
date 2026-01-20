using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ComCross.Shared.Services;
using ComCross.Shared.Interfaces;
using ComCross.Core.Services;
using ComCross.Core.Application;
using ComCross.Platform.SharedMemory;

namespace ComCross.Core.Extensions;

/// <summary>
/// Extension methods for registering core business services.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComCrossCore(this IServiceCollection services)
    {
        // Application Host
        services.AddSingleton<IAppHost, AppHost>();

        // Logging Infrastructure
        services.AddSingleton<ILoggerFactory, AppLogLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(AppLogLogger<>));

        // Core Business Services (Singleton)
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<IExtensibleLocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBus>());
        
        services.AddSingleton<ConfigService>();
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AppLogService>();
        services.AddSingleton<LogStorageService>();
        
        services.AddSingleton<MessageStreamService>();
        services.AddSingleton<IMessageStreamService>(sp => sp.GetRequiredService<MessageStreamService>());
        
        services.AddSingleton<DeviceService>();
        
        services.AddSingleton<WorkspaceDatabaseService>();
        services.AddSingleton<WorkspaceMigrationService>();
        services.AddSingleton<WorkloadService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<CommandService>();
        
        // Business Coordinators (Domain Facades)
        services.AddSingleton<IWorkspaceCoordinator, WorkspaceCoordinator>();
        
        // Plugin Runtime System
        services.AddSingleton<PluginDiscoveryService>();
        services.AddSingleton<PluginRuntimeService>();
        services.AddSingleton<PluginManagerService>();
        services.AddSingleton<PluginHostProtocolService>();
        services.AddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();

        // Shared Memory (IPC)
        services.AddSingleton(new SharedMemoryConfig());
        services.AddSingleton<ISharedMemoryMapFactory, SharedMemoryMapFactory>();
        services.AddSingleton<SharedMemoryManager>();
        services.AddSingleton<SharedMemoryReader>();
        services.AddSingleton<SharedMemorySessionService>();

        // Plugin UI State (Part of Core Logic)
        services.AddSingleton<ComCross.PluginSdk.UI.PluginUiStateManager>();
        services.AddSingleton<ComCross.PluginSdk.UI.PluginActionExecutor>();

        // Host-side persistence for plugin UI defaults/last-used values
        services.AddSingleton<PluginUiConfigService>();

        return services;
    }
}
