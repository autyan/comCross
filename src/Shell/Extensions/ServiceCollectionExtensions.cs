using Microsoft.Extensions.DependencyInjection;
using ComCross.Core.Extensions;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;

namespace ComCross.Shell.Extensions;

/// <summary>
/// Extension methods for configuring UI-specific services in the Shell project.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComCrossShell(this IServiceCollection services)
    {
        // 1. Register Core Services (Business Logic)
        services.AddComCrossCore();

        // 2. Register Shell-specific UI Implementations of Core Interfaces
        services.AddSingleton<ComCross.PluginSdk.UI.IPluginUiControlFactory, ComCross.Shell.Plugins.UI.AvaloniaPluginUiControlFactory>();
        services.AddSingleton<ComCross.PluginSdk.UI.PluginUiRenderer, ComCross.Shell.Plugins.UI.AvaloniaPluginUiRenderer>();
        services.AddSingleton<ComCross.PluginSdk.UI.IPluginCommunicationLink, ComCross.Shell.Plugins.UI.ShellPluginCommunicationLink>();
        
        // 3. ViewModels (Transient)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DisplaySettingsViewModel>();
        services.AddTransient<ProgressDialogViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CommandCenterViewModel>();
        services.AddTransient<NotificationCenterViewModel>();
        services.AddTransient<PluginManagerViewModel>();
        services.AddTransient<BusAdapterSelectorViewModel>();
        services.AddTransient<WorkloadPanelViewModel>();
        services.AddTransient<WorkloadTabsViewModel>();
        // LocalizedStringsViewModel is deprecated; use L[key] (ILocalizationStrings) directly.
        
        // 4. Views (Transient)
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
