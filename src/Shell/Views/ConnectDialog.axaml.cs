using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;
using ComCross.PluginSdk.UI;
using ComCross.Shell.Plugins.UI;

namespace ComCross.Shell.Views;

public partial class ConnectDialog : BaseWindow
{
    private readonly PluginUiRenderer _uiRenderer;
    private readonly PluginUiStateManager _stateManager;
    private readonly PluginActionExecutor _actionExecutor;
    private PluginUiAction? _primaryAction;
    private IPluginUiContainer? _currentContainer;

    /// <summary>
    /// Parameterless constructor for Avalonia XAML loader
    /// </summary>
    public ConnectDialog()
    {
        InitializeComponent();
        
        // Use service locator fallback for design-time or XAML-loader instantiation
        _uiRenderer = App.ServiceProvider.GetRequiredService<PluginUiRenderer>();
        _stateManager = App.ServiceProvider.GetRequiredService<PluginUiStateManager>();
        _actionExecutor = App.ServiceProvider.GetRequiredService<PluginActionExecutor>();
        
        Opened += async (_, _) => await OnDialogOpenedAsync();
    }

    public ConnectDialog(
        PluginUiRenderer uiRenderer,
        PluginUiStateManager stateManager,
        PluginActionExecutor actionExecutor)
    {
        _uiRenderer = uiRenderer;
        _stateManager = stateManager;
        _actionExecutor = actionExecutor;
        
        InitializeComponent();
        Opened += async (_, _) => await OnDialogOpenedAsync();
    }

    private async System.Threading.Tasks.Task OnDialogOpenedAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var options = vm.PluginManager.GetAllCapabilityOptions();
        if (options.Count == 0)
        {
            await Shell.Services.MessageBoxService.ShowWarningAsync(
                vm.L["dialog.connect.plugin.title"],
                vm.L["dialog.connect.plugin.noPlugins"]);
            Close();
            return;
        }

        PluginCapabilityComboBox.ItemsSource = options;
        PluginCapabilityComboBox.ItemTemplate = new FuncDataTemplate<PluginCapabilityLaunchOption>((item, _) =>
        {
            return new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = $"{item.PluginName} / {item.CapabilityName}" },
                    new TextBlock { Text = $"{item.PluginId}::{item.CapabilityId}", FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray }
                }
            };
        });

        var preferred = options.FirstOrDefault(o =>
            string.Equals(o.PluginId, vm.BusAdapterSelectorViewModel.SelectedAdapter?.PluginId, StringComparison.Ordinal) &&
            string.Equals(o.CapabilityId, vm.BusAdapterSelectorViewModel.SelectedAdapter?.CapabilityId, StringComparison.Ordinal));

        PluginCapabilityComboBox.SelectedItem = preferred ?? options.FirstOrDefault();

        PluginCapabilityComboBox.SelectionChanged += async (_, _) =>
        {
            var selected = (PluginCapabilityLaunchOption?)PluginCapabilityComboBox.SelectedItem;
            await LoadCapabilityUiAsync(vm, selected);
        };

        await LoadCapabilityUiAsync(vm, (PluginCapabilityLaunchOption?)PluginCapabilityComboBox.SelectedItem);

        Action<string, PluginHostUiStateInvalidatedEvent>? invalidationHandler = null;
        invalidationHandler = (pluginId, invalidated) =>
        {
            var selected = (PluginCapabilityLaunchOption?)PluginCapabilityComboBox.SelectedItem;
            if (selected is null)
            {
                return;
            }

            if (!string.Equals(pluginId, selected.PluginId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(invalidated.CapabilityId, selected.CapabilityId, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(invalidated.ViewId) &&
                !string.Equals(invalidated.ViewId, "connect-dialog", StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.UIThread.Post(() => _ = LoadCapabilityUiAsync(vm, selected, refreshOnly: true));
        };

        vm.PluginUiStateInvalidated += invalidationHandler;
        Closed += (_, _) =>
        {
            if (invalidationHandler is not null)
            {
                vm.PluginUiStateInvalidated -= invalidationHandler;
            }
        };
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var selected = (PluginCapabilityLaunchOption?)PluginCapabilityComboBox.SelectedItem;
        if (selected is null)
        {
            return;
        }

        try
        {
            // 在新架构中，Connect 操作会自动带上 StateManager 里的所有当前 UI 状态
            await _actionExecutor.ExecuteConnectAsync(selected.PluginId, selected.CapabilityId, null);
            Close();
        }
        catch (Exception ex)
        {
            await Shell.Services.MessageBoxService.ShowErrorAsync(vm.L["dialog.connect.title"], ex.Message);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async System.Threading.Tasks.Task LoadCapabilityUiAsync(
        MainWindowViewModel vm,
        PluginCapabilityLaunchOption? selected,
        bool refreshOnly = false)
    {
        if (selected is null)
        {
            return;
        }

        try
        {
            SchemaHintText.IsVisible = false;

            // 获取 UI 状态（硬件枚举等）
            var uiState = await vm.PluginManager.TryGetUiStateAsync(
                selected.PluginId,
                selected.CapabilityId,
                sessionId: null,
                viewId: "connect-dialog",
                timeout: TimeSpan.FromSeconds(2));

            // 更新 StateManager
            if (uiState != null && uiState.Value.ValueKind == JsonValueKind.Object)
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(uiState.Value.GetRawText());
                if (dict != null)
                {
                    _stateManager.UpdateStates(null, dict);
                }
            }

            var uiSchema = PluginUiSchema.TryParse(selected.UiSchema);
            var jsonSchema = JsonSchemaView.TryParse(selected.JsonSchema);

            var fields = uiSchema?.Fields;
            if (fields is null || fields.Count == 0)
            {
                // 暂时使用旧的 ToDefaultFields 逻辑来转换
                fields = ToDefaultFields(jsonSchema);
            }

            if (fields is null || fields.Count == 0)
            {
                SchemaHintText.Text = "This capability does not provide a usable UI schema.";
                SchemaHintText.IsVisible = true;
                return;
            }

            if (uiSchema != null)
            {
                 if (uiSchema.TitleKey is { Length: > 0 })
                {
                    Title = vm.L[uiSchema.TitleKey];
                }

                _primaryAction = uiSchema.Actions?.FirstOrDefault(a => string.Equals(a.Id, "connect", StringComparison.Ordinal))
                                 ?? uiSchema.Actions?.FirstOrDefault();
            }

            if (_primaryAction?.LabelKey is { Length: > 0 })
            {
                ConnectButton.Content = vm.L[_primaryAction.LabelKey];
            }
            else
            {
                ConnectButton.Content = vm.L["dialog.connect.connect"];
            }

            // 使用新架构渲染
            var schema = new PluginUiSchema { Fields = fields! };
            var container = _uiRenderer.GetOrRender(selected.PluginId, selected.CapabilityId, schema, null, "connect-dialog");
            
            if (container is AvaloniaPluginUiContainer avaloniaContainer)
            {
                DynamicFieldsPanel.Children.Clear();
                DynamicFieldsPanel.Children.Add(avaloniaContainer.GetPanel());
                _currentContainer = container;
            }
        }
        catch (Exception ex)
        {
            SchemaHintText.Text = ex.Message;
            SchemaHintText.IsVisible = true;
        }
    }

    private static List<PluginUiField>? ToDefaultFields(JsonSchemaView? jsonSchema)
    {
        if (jsonSchema == null) return null;
        return jsonSchema.ToDefaultFields()?.ToList();
    }

    private sealed class JsonSchemaView
    {
        private readonly JsonElement _schema;

        private JsonSchemaView(JsonElement schema)
        {
            _schema = schema;
        }

        public static JsonSchemaView? TryParse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return new JsonSchemaView(doc.RootElement.Clone());
            }
            catch
            {
                return null;
            }
        }

        public List<string> GetEnumOptions(string propertyName)
        {
            try
            {
                if (!_schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                {
                    return new List<string>();
                }

                if (!props.TryGetProperty(propertyName, out var propSchema) || propSchema.ValueKind != JsonValueKind.Object)
                {
                    return new List<string>();
                }

                if (!propSchema.TryGetProperty("enum", out var enums) || enums.ValueKind != JsonValueKind.Array)
                {
                    return new List<string>();
                }

                var list = new List<string>();
                foreach (var item in enums.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            list.Add(s);
                        }
                    }
                }

                return list;
            }
            catch
            {
                return new List<string>();
            }
        }

        public IReadOnlyList<PluginUiField>? ToDefaultFields()
        {
            try
            {
                if (!_schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var fields = new List<PluginUiField>();
                foreach (var prop in props.EnumerateObject())
                {
                    var control = "text";
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (prop.Value.TryGetProperty("enum", out var enums) && enums.ValueKind == JsonValueKind.Array)
                        {
                            control = "select";
                        }
                        else if (prop.Value.TryGetProperty("type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String)
                        {
                            var type = typeNode.GetString();
                            if (string.Equals(type, "integer", StringComparison.Ordinal) || string.Equals(type, "number", StringComparison.Ordinal))
                            {
                                control = "number";
                            }
                        }
                    }

                    fields.Add(new PluginUiField { 
                        Key = prop.Name, 
                        Name = prop.Name, 
                        Control = control, 
                        Type = control, 
                        EnumFromSchema = true 
                    });
                }

                return fields;
            }
            catch
            {
                return null;
            }
        }
    }
}
