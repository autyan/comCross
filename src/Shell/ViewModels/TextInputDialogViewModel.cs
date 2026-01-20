using ComCross.Shared.Services;

namespace ComCross.Shell.ViewModels;

public sealed class TextInputDialogViewModel : BaseViewModel
{
    private string _text;

    public TextInputDialogViewModel(
        ILocalizationService localization,
        string title,
        string label,
        string text,
        string? watermark,
        string okText,
        string cancelText)
        : base(localization)
    {
        Title = title;
        Label = label;
        _text = text;
        Watermark = watermark;
        OkText = okText;
        CancelText = cancelText;
    }

    public string Title { get; }
    public string Label { get; }
    public string? Watermark { get; }
    public string OkText { get; }
    public string CancelText { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }
}
