using ComCross.Shared.Services;
using System;

namespace ComCross.Shell.ViewModels;

public sealed class RenameWorkloadDialogViewModel : BaseViewModel
{
    private readonly string _originalName;
    private string _workloadName;
    private string? _nameError;

    public RenameWorkloadDialogViewModel(ILocalizationService localization, string currentName)
        : base(localization)
    {
        _originalName = currentName ?? string.Empty;
        _workloadName = currentName ?? string.Empty;

        ValidateName();
        OnPropertyChanged(nameof(IsValid));
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

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(WorkloadName)
        && string.IsNullOrEmpty(NameError)
        && !string.Equals(WorkloadName.Trim(), _originalName.Trim(), StringComparison.Ordinal);

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
        else if (string.Equals(WorkloadName.Trim(), _originalName.Trim(), StringComparison.Ordinal))
        {
            NameError = L["dialog.renameWorkload.error.sameName"];
        }
        else
        {
            NameError = null;
        }
    }
}
