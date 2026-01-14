using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ComCross.Shell.ViewModels;

namespace ComCross.Shell.Views;

public partial class CommandCenterView : UserControl
{
    public CommandCenterView()
    {
        InitializeComponent();
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            await vm.SendSelectedAsync();
        }
    }

    private void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            vm.NewCommand();
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            await vm.SaveAsync();
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            await vm.DeleteSelectedAsync();
        }
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null)
            {
                await vm.ImportAsync();
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = vm.LocalizedStrings.ToolCommandsImport,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("JSON")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

            var file = files.Count > 0 ? files[0] : null;
            if (file != null)
            {
                await vm.ImportAsync(file.Path.LocalPath);
            }
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommandCenterViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null)
            {
                await vm.ExportAsync();
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = vm.LocalizedStrings.ToolCommandsExport,
                SuggestedFileName = "commands.json",
                DefaultExtension = "json",
                FileTypeChoices =
                [
                    new FilePickerFileType("JSON")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });

            if (file != null)
            {
                await vm.ExportAsync(file.Path.LocalPath);
            }
        }
    }
}
