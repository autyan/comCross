using Microsoft.Extensions.DependencyInjection;
using ComCross.Shared.Services;
using ComCross.Shared.Interfaces;
using ComCross.Core.Services;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using ComCross.Adapters.Serial;
using Microsoft.Extensions.Logging;
using ComCross.Platform.SharedMemory;

namespace ComCross.Shell.Extensions;

/// <summary>
/// Extension methods for configuring ComCross services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComCrossServices(this IServiceCollection services)
    {
        // Logging (bridged to AppLogService; settings-controlled)
        services.AddSingleton<ILoggerFactory, AppLogLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(AppLogLogger<>));

        // Core Services (Singleton)
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ILocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<IExtensibleLocalizationService>(sp => sp.GetRequiredService<LocalizationService>());
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<EventBus>());
        services.AddSingleton<ConfigService>();
        services.AddSingleton<AppDatabase>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<MessageStreamService>();
        services.AddSingleton<IMessageStreamService>(sp => sp.GetRequiredService<MessageStreamService>());
        services.AddSingleton<DeviceService>();
        services.AddSingleton<SerialAdapter>();
        services.AddSingleton<IDeviceAdapter>(sp => sp.GetRequiredService<SerialAdapter>());
        services.AddSingleton<WorkspaceDatabaseService>();
        services.AddSingleton<WorkspaceMigrationService>();
        services.AddSingleton<WorkloadService>();
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<AppLogService>();
        services.AddSingleton<LogStorageService>();
        services.AddSingleton<CommandService>();
        services.AddSingleton<PluginDiscoveryService>();
        services.AddSingleton<PluginRuntimeService>();
        services.AddSingleton<PluginManagerService>();
        services.AddSingleton<PluginHostProtocolService>();
        services.AddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();

        // Plugin UI Rendering System (New v0.4.0)
        services.AddSingleton<ComCross.PluginSdk.UI.PluginUiStateManager>();
        services.AddSingleton<ComCross.PluginSdk.UI.IPluginUiControlFactory, ComCross.Shell.Plugins.UI.AvaloniaPluginUiControlFactory>();
        services.AddSingleton<ComCross.PluginSdk.UI.PluginUiRenderer, ComCross.Shell.Plugins.UI.AvaloniaPluginUiRenderer>();
        services.AddSingleton<ComCross.PluginSdk.UI.IPluginCommunicationLink, ComCross.Shell.Plugins.UI.ShellPluginCommunicationLink>();
        services.AddSingleton<ComCross.PluginSdk.UI.PluginActionExecutor>();

        // Shared memory (ADR-010)
        services.AddSingleton(new SharedMemoryConfig());
        services.AddSingleton<ISharedMemoryMapFactory, SharedMemoryMapFactory>();
        services.AddSingleton<SharedMemoryManager>();
        services.AddSingleton<SharedMemoryReader>();
        services.AddSingleton<SharedMemorySessionService>();
        
        // ViewModels (Transient)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CommandCenterViewModel>();
        services.AddTransient<NotificationCenterViewModel>();
        services.AddTransient<PluginManagerViewModel>();
        services.AddTransient<BusAdapterSelectorViewModel>();
        services.AddTransient<WorkloadPanelViewModel>();
        services.AddTransient<WorkloadTabsViewModel>();
        services.AddTransient<LocalizedStringsViewModel>();
        
        // Views (Transient)
        services.AddTransient<MainWindow>();
        services.AddTransient<LeftSidebar>();
        services.AddTransient<MessageStreamView>();
        services.AddTransient<RightToolDock>();
        services.AddTransient<SettingsView>();
        services.AddTransient<NotificationCenterView>();
        services.AddTransient<CommandCenterView>();
        services.AddTransient<PluginManagerView>();
        services.AddTransient<WorkloadPanel>();
        services.AddTransient<WorkloadTabs>();
        services.AddTransient<BusAdapterSelector>();
        services.AddTransient<SerialConfigPanel>();
        services.AddTransient<TestWindow>();
        services.AddTransient<CreateWorkloadDialog>();
        services.AddTransient<RenameWorkloadDialog>();
        services.AddTransient<ConnectDialog>();
        
        return services;
    }
}
