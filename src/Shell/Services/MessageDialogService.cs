using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

public sealed class MessageDialogService : IMessageDialogService
{
    private readonly ILocalizationService _localization;
    private readonly IMessageBoxDialogFactory _dialogFactory;

    public MessageDialogService(
        ILocalizationService localization,
        IMessageBoxDialogFactory dialogFactory)
    {
        _localization = localization;
        _dialogFactory = dialogFactory;
    }

    public async Task ShowErrorAsync(Window owner, string title, string message)
    {
        var okText = _localization.GetString("messagebox.ok");
        await _dialogFactory.ShowMessageAsync(owner, title, message, MessageBoxIcon.Error, okText);
    }

    public async Task ShowWarningAsync(Window owner, string title, string message)
    {
        var okText = _localization.GetString("messagebox.ok");
        await _dialogFactory.ShowMessageAsync(owner, title, message, MessageBoxIcon.Warning, okText);
    }

    public async Task ShowInfoAsync(Window owner, string title, string message)
    {
        var okText = _localization.GetString("messagebox.ok");
        await _dialogFactory.ShowMessageAsync(owner, title, message, MessageBoxIcon.Info, okText);
    }

    public async Task<bool> ShowConfirmAsync(
        Window owner,
        string title,
        string message,
        MessageBoxIcon icon = MessageBoxIcon.Question)
    {
        var okText = _localization.GetString("messagebox.ok");
        var cancelText = _localization.GetString("messagebox.cancel");
        var result = await _dialogFactory.ShowCustomAsync(owner, title, message, icon, okText, cancelText);
        return result == 0;
    }
}
