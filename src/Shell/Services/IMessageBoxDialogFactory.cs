using System.Threading.Tasks;
using Avalonia.Controls;

namespace ComCross.Shell.Services;

public interface IMessageBoxDialogFactory
{
    Task<int> ShowCustomAsync(Window owner, string title, string message, MessageBoxIcon icon, params string[] buttons);
    Task ShowMessageAsync(Window owner, string title, string message, MessageBoxIcon icon, params string[] buttons);
}
