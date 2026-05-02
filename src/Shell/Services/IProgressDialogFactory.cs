using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Services;

public interface IProgressDialogFactory
{
    ProgressDialogViewModel CreateViewModel();
    Task<Window> ShowAsync(ProgressDialogViewModel viewModel, Window? owner = null);
}
