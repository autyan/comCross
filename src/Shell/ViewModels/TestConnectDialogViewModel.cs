using ComCross.Shared.Services;
using System.Collections.Generic;

namespace ComCross.Shell.ViewModels;

public sealed class TestConnectDialogViewModel : BaseViewModel
{
    private CapabilityOption? _selectedOption;
    private string _parametersJson = "{}";

    public TestConnectDialogViewModel(ILocalizationService localization, IReadOnlyList<CapabilityOption> options)
        : base(localization)
    {
        Options = options;

        if (options.Count > 0)
        {
            SelectedOption = options[0];
            var defaultJson = options[0].DefaultParametersJson;
            ParametersJson = string.IsNullOrWhiteSpace(defaultJson) ? "{}" : defaultJson;
        }
    }

    public IReadOnlyList<CapabilityOption> Options { get; }

    public CapabilityOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (!SetProperty(ref _selectedOption, value))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ParametersJson) && value != null)
            {
                var defaultJson = value.DefaultParametersJson;
                ParametersJson = string.IsNullOrWhiteSpace(defaultJson) ? "{}" : defaultJson;
            }
        }
    }

    public string ParametersJson
    {
        get => _parametersJson;
        set => SetProperty(ref _parametersJson, value);
    }
}
