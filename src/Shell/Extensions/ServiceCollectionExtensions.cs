using Microsoft.Extensions.DependencyInjection;
using ComCross.Core.Extensions;
using ComCross.Shell.Services;
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

        // Cross-cutting factories (avoid manual `new` for Views/ViewModels)
        services.AddTransient<IObjectFactory, ComCross.Shell.Services.ObjectFactory>();

        // Dialog factories
        services.AddTransient<ITextInputDialogFactory, TextInputDialogFactory>();
        services.AddTransient<IMessageBoxDialogFactory, MessageBoxDialogFactory>();
        services.AddTransient<IProgressDialogFactory, ProgressDialogFactory>();
        
        // 3. ViewModels (Scoped to MainWindow scope)
        services.AddScoped<MainWindowViewModel>();
        services.AddScoped<LeftSidebarViewModel>();
        services.AddScoped<MessageStreamViewModel>();
        services.AddScoped<RightToolDockViewModel>();
        services.AddScoped<DisplaySettingsViewModel>();
        services.AddScoped<ProgressDialogViewModel>();
        services.AddScoped<SessionsViewModel>();
        services.AddScoped<SettingsViewModel>();
        services.AddScoped<CommandCenterViewModel>();
        services.AddScoped<NotificationCenterViewModel>();
        services.AddScoped<PluginManagerViewModel>();
        services.AddScoped<BusAdapterSelectorViewModel>();
        services.AddScoped<WorkloadPanelViewModel>();
        services.AddScoped<WorkloadTabsViewModel>();
        services.AddTransient<CreateWorkloadDialogViewModel>();
        services.AddTransient<RenameWorkloadDialogViewModel>();
        services.AddTransient<TestConnectDialogViewModel>();
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
        services.AddTransient<TestWindow>();
        services.AddTransient<CreateWorkloadDialog>();
        services.AddTransient<RenameWorkloadDialog>();
        services.AddTransient<ConnectDialog>();
        services.AddTransient<TestConnectDialog>();
        services.AddTransient<TextInputDialog>();
        services.AddTransient<ProgressDialogWindow>();
        services.AddTransient<MessageBoxDialogWindow>();
        
        return services;
    }
}
