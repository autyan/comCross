using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Services;

public sealed class ProgressDialogFactory : IProgressDialogFactory
{
    private readonly IServiceProvider _services;

    public ProgressDialogFactory(IServiceProvider services)
    {
        _services = services;
    }

    public ProgressDialogViewModel CreateViewModel()
        => ActivatorUtilities.CreateInstance<ProgressDialogViewModel>(_services);

    public async Task<Window> ShowAsync(ProgressDialogViewModel viewModel, Window? owner = null)
    {
        var resolvedOwner = owner;
        if (resolvedOwner is null && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            resolvedOwner = desktop.MainWindow;
        }

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var dialog = _services.GetRequiredService<ProgressDialogWindow>();
            dialog.DataContext = viewModel;

            if (resolvedOwner is not null)
            {
                dialog.Show(resolvedOwner);
            }
            else
            {
                dialog.Show();
            }

            return (Window)dialog;
        });
    }
}
