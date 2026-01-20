using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class CreateWorkloadDialogViewModel : BaseViewModel
{
    private string _workloadName = string.Empty;
    private string _workloadDescription = string.Empty;
    private string? _nameError;

    public CreateWorkloadDialogViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public string WorkloadName
    {
        get => _workloadName;
        set
        {
            if (!SetProperty(ref _workloadName, value))
            {
                return;
            }

            ValidateName();
            OnPropertyChanged(nameof(IsValid));
        }
    }

    public string WorkloadDescription
    {
        get => _workloadDescription;
        set
        {
            if (!SetProperty(ref _workloadDescription, value))
            {
                return;
            }

            OnPropertyChanged(nameof(DescriptionLength));
        }
    }

    public string? NameError
    {
        get => _nameError;
        private set
        {
            if (!SetProperty(ref _nameError, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsValid));
        }
    }

    public int DescriptionLength => WorkloadDescription?.Length ?? 0;

    public bool IsValid => !string.IsNullOrWhiteSpace(WorkloadName) && string.IsNullOrEmpty(NameError);

    private void ValidateName()
    {
        if (string.IsNullOrWhiteSpace(WorkloadName))
        {
            NameError = L["dialog.createWorkload.error.empty"];
        }
        else if (WorkloadName.Length < 2)
        {
            NameError = L["dialog.createWorkload.error.minLength"];
        }
        else if (WorkloadName.Length > 50)
        {
            NameError = L["dialog.createWorkload.error.maxLength"];
        }
        else
        {
            NameError = null;
        }
    }
}
