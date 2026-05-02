using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Services;

public sealed class MessageBoxDialogFactory : IMessageBoxDialogFactory
{
    private readonly IServiceProvider _services;

    public MessageBoxDialogFactory(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<int> ShowCustomAsync(Window owner, string title, string message, MessageBoxIcon icon, params string[] buttons)
    {
        var dialog = CreateWindow(title, message, icon, buttons);
        var result = await dialog.ShowDialog<int?>(owner);
        return result ?? -1;
    }

    public async Task ShowMessageAsync(Window owner, string title, string message, MessageBoxIcon icon, params string[] buttons)
    {
        var dialog = CreateWindow(title, message, icon, buttons);
        await dialog.ShowDialog(owner);
    }

    private MessageBoxDialogWindow CreateWindow(string title, string message, MessageBoxIcon icon, string[] buttons)
    {
        var dialog = _services.GetRequiredService<MessageBoxDialogWindow>();
        dialog.DataContext = ActivatorUtilities.CreateInstance<MessageBoxDialogViewModel>(
            _services,
            title,
            message,
            icon,
            buttons);
        return dialog;
    }
}
