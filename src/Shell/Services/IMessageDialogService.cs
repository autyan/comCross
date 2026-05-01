using System.Threading.Tasks;
using Avalonia.Controls;

namespace ComCross.Shell.Services;

public interface IMessageDialogService
{
    Task ShowErrorAsync(Window owner, string title, string message);

    Task ShowWarningAsync(Window owner, string title, string message);

    Task ShowInfoAsync(Window owner, string title, string message);

    Task<bool> ShowConfirmAsync(
        Window owner,
        string title,
        string message,
        MessageBoxIcon icon = MessageBoxIcon.Question);
}
