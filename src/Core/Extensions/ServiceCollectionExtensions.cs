using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ComCross.Shared.Services;
using ComCross.Shared.Interfaces;
using ComCross.Core.Services;
using ComCross.Core.Application;
using ComCross.Platform.SharedMemory;
using ComCross.Platform.UserDirectories;

namespace ComCross.Core.Extensions;

/// <summary>
/// Extension methods for registering core business services.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComCrossCore(
        this IServiceCollection services,
        ComCrossInstanceIdentity? instance = null)
    {
        instance ??= ComCrossInstanceIdentity.Stable();

        // Application Host
        services.AddSingleton<IAppHost, AppHost>();
        services.AddSingleton(instance);
        services.AddSingleton<IPlatformUserDirectoryProvider, PlatformUserDirectoryProvider>();

        // Logging Infrastructure
        services.AddSingleton<ILoggerFactory, AppLogLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(AppLogLogger<>));

        // Core Business Services (Singleton)
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<IExtensibleLocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBus>());

        services.AddSingleton(sp => new ComCrossPathService(
            sp.GetRequiredService<IPlatformUserDirectoryProvider>(),
            sp.GetRequiredService<ComCrossInstanceIdentity>()));
        services.AddSingleton(sp => new ConfigService(sp.GetRequiredService<ComCrossPathService>()));
        services.AddSingleton(sp => new AppDatabase(sp.GetRequiredService<ComCrossPathService>()));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AppLogService>();
        services.AddSingleton<LogStorageService>();
        
        services.AddSingleton<MessageStreamService>();
        services.AddSingleton<IMessageStreamService>(sp => sp.GetRequiredService<MessageStreamService>());
        
        services.AddSingleton<DeviceService>();
        
        // Plugin trust / signature verification (disabled by default; see AppSettings.Plugins.SignatureVerification)
        services.AddSingleton<PluginSignatureVerificationService>();
        
        services.AddSingleton<WorkspaceDatabaseService>();
        services.AddSingleton<WorkspaceMigrationService>();
        services.AddSingleton<WorkspaceStateStore>();
        services.AddSingleton<WorkloadService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<CommandDefaultService>();
        services.AddSingleton<CommandExecutionService>();

        // Persist committed session definitions (last successful connect parameters)
        services.AddSingleton<SessionDescriptorPersistenceService>();
        
        // Business Coordinators (Domain Services)
        services.AddSingleton<IWorkspaceCoordinator, WorkspaceCoordinator>();
        
        // Plugin Runtime System
        services.AddSingleton<PluginDiscoveryService>();
        services.AddSingleton<BundledPluginSynchronizationService>();
        services.AddSingleton<PluginRuntimeService>();
        services.AddSingleton<ExtensionRuntimeService>();
        services.AddSingleton<SessionHostRuntimeService>();
        services.AddSingleton<PluginManagerService>();
        services.AddSingleton<PluginHostProtocolService>();
        services.AddSingleton<PluginResourceQueryService>();
        services.AddSingleton<PluginSessionStorageService>();
        services.AddSingleton<PluginSessionInitializationService>();
        services.AddSingleton<SessionDataCleanupService>();
        services.AddSingleton<IPluginUiStateFetcher, PluginUiStateFetcher>();
        services.AddSingleton<IExtensionActionExecutor, ExtensionActionExecutor>();
        services.AddSingleton<PluginHostEventRouterService>();
        services.AddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();

        // Plugin-managed resource orchestration.
        services.AddSingleton<ManagedResourceAutoConnectService>();

        // Shared Memory (IPC)
        services.AddSingleton(new SharedMemoryConfig());
        services.AddSingleton<ISharedMemoryMapFactory, SharedMemoryMapFactory>();
        services.AddSingleton<SharedMemoryManager>();
        services.AddSingleton<IFrameStore, InMemoryFrameStore>();
        services.AddSingleton<SharedMemoryIngestService>();
        services.AddSingleton<SharedMemorySessionService>();
        services.AddSingleton<FrameStoreMessageStreamPumpService>();
        services.AddSingleton<SharedMemoryBackpressureBridgeService>();
        services.AddSingleton<ExtensionBridgeService>();

        // Plugin UI State (Part of Core Logic)
        services.AddSingleton<ComCross.PluginSdk.UI.PluginUiStateManager>();
        services.AddSingleton<ComCross.PluginSdk.UI.PluginActionExecutor>();

        // Host-side persistence for plugin UI defaults/last-used values
        services.AddSingleton<PluginUiConfigService>();

        return services;
    }
}
