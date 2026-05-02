using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ComCross.Shell.ViewModels;
using ComCross.Shell.Views;
using ComCross.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ComCross.Shell.Services;

public sealed class TextInputDialogFactory : ITextInputDialogFactory
{
    private readonly IServiceProvider _services;

    public TextInputDialogFactory(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<string?> ShowAsync(
        Window owner,
        ILocalizationService localization,
        string title,
        string label,
        string text,
        string? watermark,
        string okText,
        string cancelText)
    {
        var dialog = _services.GetRequiredService<TextInputDialog>();
        dialog.DataContext = ActivatorUtilities.CreateInstance<TextInputDialogViewModel>(
            _services,
            localization,
            title,
            label,
            text,
            watermark ?? string.Empty,
            okText,
            cancelText);

        return await dialog.ShowDialog<string?>(owner);
    }
}
