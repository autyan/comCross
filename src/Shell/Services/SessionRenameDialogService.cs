using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shared.Models;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Services;

public interface ISessionRenameDialogService
{
    Task<string?> ShowAsync(Window owner, Session session, MainWindowViewModel mainWindow);
}

public sealed class SessionRenameDialogService : ISessionRenameDialogService
{
    private readonly ITextInputDialogFactory _textInputDialogFactory;

    public SessionRenameDialogService(ITextInputDialogFactory textInputDialogFactory)
    {
        _textInputDialogFactory = textInputDialogFactory;
    }

    public Task<string?> ShowAsync(Window owner, Session session, MainWindowViewModel mainWindow)
        => _textInputDialogFactory.ShowAsync(
            owner,
            mainWindow.Localization,
            mainWindow.L["dialog.renameSession.title"],
            mainWindow.L["dialog.renameSession.label"],
            session.Name,
            mainWindow.L["dialog.renameSession.placeholder"],
            mainWindow.L["dialog.renameSession.ok"],
            mainWindow.L["dialog.renameSession.cancel"]);
}
