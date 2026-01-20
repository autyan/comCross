using System.Collections.Generic;
using System.Linq;

namespace ComCross.Shell.ViewModels;

public sealed class MessageBoxDialogViewModel
{
    public MessageBoxDialogViewModel(string title, string message, Services.MessageBoxIcon icon, IReadOnlyList<string> buttons)
    {
        Title = title;
        Message = message;
        IconText = icon switch
        {
            Services.MessageBoxIcon.Error => "❌",
            Services.MessageBoxIcon.Warning => "⚠️",
            Services.MessageBoxIcon.Info => "ℹ️",
            Services.MessageBoxIcon.Question => "❓",
            _ => string.Empty
        };

        Buttons = buttons
            .Select((text, index) => new MessageBoxDialogButton(index, text))
            .ToArray();
    }

    public string Title { get; }
    public string Message { get; }
    public string IconText { get; }

    public IReadOnlyList<MessageBoxDialogButton> Buttons { get; }
}

public sealed record MessageBoxDialogButton(int Index, string Text);
