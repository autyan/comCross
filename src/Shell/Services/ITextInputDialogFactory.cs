using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shared.Services;

namespace ComCross.Shell.Services;

public interface ITextInputDialogFactory
{
    Task<string?> ShowAsync(
        Window owner,
        ILocalizationService localization,
        string title,
        string label,
        string text,
        string? watermark,
        string okText,
        string cancelText);
}
