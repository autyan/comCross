using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace ComCross.Shell.ViewModels;

public sealed class MessageBoxDialogViewModel
{
    public MessageBoxDialogViewModel(string title, string message, Services.MessageBoxIcon icon, IReadOnlyList<string> buttons)
    {
        Title = title;
        Message = message;
        IconData = Geometry.Parse(icon switch
        {
            Services.MessageBoxIcon.Error => "M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20M15 9l-6 6M9 9l6 6",
            Services.MessageBoxIcon.Warning => "M12 3l10 18H2zM12 9v5M12 17h.01",
            Services.MessageBoxIcon.Info => "M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20M12 10v6M12 7h.01",
            Services.MessageBoxIcon.Question => "M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20M9.5 9a2.5 2.5 0 1 1 4.2 1.84c-.92.73-1.7 1.18-1.7 2.66M12 17h.01",
            _ => "M0 0"
        });
        IconBrush = new SolidColorBrush(Color.Parse(
            icon == Services.MessageBoxIcon.Error || icon == Services.MessageBoxIcon.Warning
                ? "#F59E0B"
                : "#3FA7FF"));

        Buttons = buttons
            .Select((text, index) => new MessageBoxDialogButton(index, text))
            .ToArray();
    }

    public string Title { get; }
    public string Message { get; }
    public Geometry IconData { get; }
    public IBrush IconBrush { get; }

    public IReadOnlyList<MessageBoxDialogButton> Buttons { get; }
}

public sealed record MessageBoxDialogButton(int Index, string Text);
