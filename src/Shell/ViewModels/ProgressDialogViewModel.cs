using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

/// <summary>
/// ViewModel for shutdown/cleanup progress dialog.
/// </summary>
public sealed class ProgressDialogViewModel : BaseViewModel
{
    private string _currentStatus = string.Empty;

    public ProgressDialogViewModel(ILocalizationService localization)
        : base(localization)
    {
        _currentStatus = L["shutdown.cleaningUp"];
    }

    public string CurrentStatus
    {
        get => _currentStatus;
        private set
        {
            SetProperty(ref _currentStatus, value);
        }
    }

    public void UpdateStatus(string status)
    {
        CurrentStatus = status;
    }
}
