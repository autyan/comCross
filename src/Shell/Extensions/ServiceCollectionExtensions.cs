using Microsoft.Extensions.DependencyInjection;
using ComCross.Shared.Services;
using ComCross.Shared.Interfaces;
using ComCross.Core.Services;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using ComCross.Adapters.Serial;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComCross.Shell.Extensions;

/// <summary>
/// Extension methods for configuring ComCross services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComCrossServices(this IServiceCollection services)
    {
        // Logging (minimal default)
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Core Services (Singleton)
        services.AddSingleton<ILocalizationService, LocalizationService>();
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
        services.AddTransient<TestWindow>();
        services.AddTransient<CreateWorkloadDialog>();
        services.AddTransient<RenameWorkloadDialog>();
        services.AddTransient<ConnectDialog>();
        
        return services;
    }
}
