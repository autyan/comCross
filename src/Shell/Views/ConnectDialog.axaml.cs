using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ComCross.Shell.ViewModels;
using ComCross.Shared.Models;

namespace ComCross.Shell.Views;

public partial class ConnectDialog : BaseWindow
{
    private readonly Dictionary<string, object?> _fieldValues = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _fieldControls = new(StringComparer.Ordinal);
    private PluginUiAction? _primaryAction;

    public ConnectDialog()
    {
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
            var connectParameters = BuildParametersJsonElementFromFields();

            if (!vm.PluginManager.TryValidateParameters(selected.PluginId, selected.CapabilityId, connectParameters, out var validateError))
            {
                await Shell.Services.MessageBoxService.ShowErrorAsync(
                    vm.L["dialog.connect.title"],
                    string.IsNullOrWhiteSpace(validateError) ? "Invalid parameters." : validateError);
                return;
            }

            var ok = await ExecutePrimaryActionAsync(vm, selected, connectParameters);
            if (ok)
            {
                Close();
            }
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

            var uiState = await vm.PluginManager.TryGetUiStateAsync(
                selected.PluginId,
                selected.CapabilityId,
                sessionId: null,
                viewId: "connect-dialog",
                timeout: TimeSpan.FromSeconds(2));

            var uiSchema = PluginUiSchema.TryParse(selected.UiSchema);
            var jsonSchema = JsonSchemaView.TryParse(selected.JsonSchema);

            // On refresh-only, keep current controls; just update option lists/defaults if possible.
            if (!refreshOnly)
            {
                _dirtyFields.Clear();
                _fieldValues.Clear();
                _fieldControls.Clear();
                DynamicFieldsPanel.Children.Clear();
            }

            if (uiSchema?.TitleKey is { Length: > 0 })
            {
                Title = vm.L[uiSchema.TitleKey];
            }

            _primaryAction = uiSchema?.Actions?.FirstOrDefault(a => string.Equals(a.Id, "connect", StringComparison.Ordinal))
                             ?? uiSchema?.Actions?.FirstOrDefault();

            if (_primaryAction?.LabelKey is { Length: > 0 })
            {
                ConnectButton.Content = vm.L[_primaryAction.LabelKey];
            }
            else
            {
                ConnectButton.Content = vm.L["dialog.connect.connect"];
            }

            // Auto-generate fields from JsonSchema if plugin didn't provide UiSchema fields.
            var fields = uiSchema?.Fields;
            if (fields is null || fields.Count == 0)
            {
                fields = jsonSchema?.ToDefaultFields();
            }

            if (fields is null || fields.Count == 0)
            {
                SchemaHintText.Text = "This capability does not provide a usable UI schema.";
                SchemaHintText.IsVisible = true;
                return;
            }

            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    continue;
                }

                if (!refreshOnly)
                {
                    RenderField(vm, field, uiState, jsonSchema);
                }
                else
                {
                    RefreshField(field, uiState, jsonSchema);
                }
            }
        }
        catch (Exception ex)
        {
            SchemaHintText.Text = ex.Message;
            SchemaHintText.IsVisible = true;
        }
    }

    private void RenderField(
        MainWindowViewModel vm,
        PluginUiField field,
        JsonElement? uiState,
        JsonSchemaView? schema)
    {
        var labelText = field.LabelKey is { Length: > 0 } ? vm.L[field.LabelKey] : field.Name;

        var container = new StackPanel { Spacing = 6 };
        container.Children.Add(new TextBlock { Text = labelText, FontWeight = Avalonia.Media.FontWeight.SemiBold });

        var defaultValue = ResolveDefaultValue(field, uiState);
        Control control = field.Control switch
        {
            "select" => CreateSelectControl(field, uiState, schema, defaultValue),
            "number" => CreateNumberControl(field, defaultValue),
            _ => CreateTextControl(field, defaultValue)
        };

        _fieldControls[field.Name] = control;
        container.Children.Add(control);
        DynamicFieldsPanel.Children.Add(container);
    }

    private void RefreshField(PluginUiField field, JsonElement? uiState, JsonSchemaView? schema)
    {
        if (!_fieldControls.TryGetValue(field.Name, out var control))
        {
            return;
        }

        if (_dirtyFields.Contains(field.Name))
        {
            return;
        }

        // Refresh select options (e.g., ports) and default selection.
        if (control is ComboBox combo && string.Equals(field.Control, "select", StringComparison.Ordinal))
        {
            var options = ResolveOptions(field, uiState, schema);
            combo.ItemsSource = options;
            var defaultValue = ResolveDefaultValue(field, uiState);
            if (defaultValue is string s && options.Contains(s, StringComparer.Ordinal))
            {
                combo.SelectedItem = s;
                _fieldValues[field.Name] = s;
            }
        }
        else if (control is TextBox tb)
        {
            var defaultValue = ResolveDefaultValue(field, uiState);
            if (defaultValue is not null)
            {
                tb.Text = defaultValue.ToString();
                _fieldValues[field.Name] = field.Control == "number" && int.TryParse(tb.Text, out var i) ? i : tb.Text;
            }
        }
    }

    private Control CreateSelectControl(PluginUiField field, JsonElement? uiState, JsonSchemaView? schema, object? defaultValue)
    {
        var combo = new ComboBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var options = ResolveOptions(field, uiState, schema);
        combo.ItemsSource = options;

        if (defaultValue is string s && options.Contains(s, StringComparer.Ordinal))
        {
            combo.SelectedItem = s;
            _fieldValues[field.Name] = s;
        }
        else if (options.Count > 0)
        {
            combo.SelectedIndex = 0;
            _fieldValues[field.Name] = combo.SelectedItem?.ToString();
        }

        combo.SelectionChanged += (_, _) =>
        {
            _dirtyFields.Add(field.Name);
            _fieldValues[field.Name] = combo.SelectedItem?.ToString();
        };

        return combo;
    }

    private Control CreateNumberControl(PluginUiField field, object? defaultValue)
    {
        var tb = new TextBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        if (defaultValue is not null)
        {
            tb.Text = defaultValue.ToString();
            if (int.TryParse(tb.Text, out var i))
            {
                _fieldValues[field.Name] = i;
            }
        }

        tb.LostFocus += (_, _) =>
        {
            _dirtyFields.Add(field.Name);
            if (int.TryParse(tb.Text, out var i))
            {
                _fieldValues[field.Name] = i;
            }
            else
            {
                _fieldValues.Remove(field.Name);
            }
        };

        return tb;
    }

    private Control CreateTextControl(PluginUiField field, object? defaultValue)
    {
        var tb = new TextBox { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        if (defaultValue is not null)
        {
            tb.Text = defaultValue.ToString();
            _fieldValues[field.Name] = tb.Text;
        }

        tb.TextChanged += (_, _) =>
        {
            _dirtyFields.Add(field.Name);
            _fieldValues[field.Name] = tb.Text;
        };

        return tb;
    }

    private JsonElement BuildParametersJsonElementFromFields()
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kvp in _fieldValues)
        {
            if (kvp.Value is null)
            {
                continue;
            }

            var s = kvp.Value as string;
            if (s is not null && string.IsNullOrWhiteSpace(s))
            {
                continue;
            }

            dict[kvp.Key] = kvp.Value;
        }

        return JsonSerializer.SerializeToElement(dict);
    }

    private async System.Threading.Tasks.Task<bool> ExecutePrimaryActionAsync(
        MainWindowViewModel vm,
        PluginCapabilityLaunchOption selected,
        JsonElement connectParameters)
    {
        var action = _primaryAction;

        // Default to plugin connect if no action declared.
        if (action is null)
        {
            return await vm.TryConnectPluginAdapterAsync(selected.PluginId, selected.CapabilityId, JsonSerializer.Serialize(connectParameters));
        }

        if (string.Equals(action.Kind, "host", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(action.HostAction))
            {
                await Shell.Services.MessageBoxService.ShowErrorAsync(vm.L["dialog.connect.title"], "Host action is missing.");
                return false;
            }

            var merged = MergeExtraParameters(action.ExtraParameters, connectParameters);
            return await vm.ExecuteHostActionAsync(action.HostAction, merged);
        }

        return await vm.TryConnectPluginAdapterAsync(selected.PluginId, selected.CapabilityId, JsonSerializer.Serialize(connectParameters));
    }

    private static JsonElement MergeExtraParameters(JsonElement? extra, JsonElement connectParameters)
    {
        if (extra is null || extra.Value.ValueKind != JsonValueKind.Object)
        {
            return connectParameters;
        }

        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var prop in extra.Value.EnumerateObject())
        {
            dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }

        if (connectParameters.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in connectParameters.EnumerateObject())
            {
                dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }
        }

        return JsonSerializer.SerializeToElement(dict);
    }

    private static object? ResolveDefaultValue(PluginUiField field, JsonElement? uiState)
    {
        if (uiState is null || uiState.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(field.DefaultStatePath) &&
            TryResolvePath(uiState.Value, field.DefaultStatePath!, out var node))
        {
            return node.ValueKind switch
            {
                JsonValueKind.String => node.GetString(),
                JsonValueKind.Number => node.TryGetInt32(out var i) ? i : (object?)node.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => node.ToString()
            };
        }

        return null;
    }

    private static List<string> ResolveOptions(PluginUiField field, JsonElement? uiState, JsonSchemaView? schema)
    {
        if (field.EnumFromSchema && schema is not null)
        {
            var fromSchema = schema.GetEnumOptions(field.Name);
            if (fromSchema.Count > 0)
            {
                return fromSchema;
            }
        }

        if (uiState is not null && uiState.Value.ValueKind == JsonValueKind.Object &&
            !string.IsNullOrWhiteSpace(field.OptionsStatePath) &&
            TryResolvePath(uiState.Value, field.OptionsStatePath!, out var node) &&
            node.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in node.EnumerateArray())
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

        return new List<string>();
    }

    private static bool TryResolvePath(JsonElement root, string path, out JsonElement value)
    {
        value = default;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var current = root;
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!current.TryGetProperty(part, out current))
            {
                return false;
            }
        }

        value = current;
        return true;
    }

    private sealed record PluginUiSchema(string? TitleKey, IReadOnlyList<PluginUiField> Fields, IReadOnlyList<PluginUiAction> Actions)
    {
        public static PluginUiSchema? TryParse(string? uiSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(uiSchemaJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(uiSchemaJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var titleKey = doc.RootElement.TryGetProperty("titleKey", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                var fields = new List<PluginUiField>();
                if (doc.RootElement.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in f.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                        var control = item.TryGetProperty("control", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(control))
                        {
                            continue;
                        }

                        var labelKey = item.TryGetProperty("labelKey", out var lk) && lk.ValueKind == JsonValueKind.String ? lk.GetString() : null;
                        var optionsPath = item.TryGetProperty("optionsStatePath", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null;
                        var defaultPath = item.TryGetProperty("defaultStatePath", out var dp) && dp.ValueKind == JsonValueKind.String ? dp.GetString() : null;
                        var enumFromSchema = item.TryGetProperty("enumFromSchema", out var efs) && (efs.ValueKind == JsonValueKind.True || efs.ValueKind == JsonValueKind.False) && efs.GetBoolean();
                        var required = item.TryGetProperty("required", out var req) && (req.ValueKind == JsonValueKind.True || req.ValueKind == JsonValueKind.False) && req.GetBoolean();

                        fields.Add(new PluginUiField(name!, control!, labelKey, optionsPath, defaultPath, enumFromSchema, required));
                    }
                }

                var actions = new List<PluginUiAction>();
                if (doc.RootElement.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in a.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var id = item.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String ? idNode.GetString() : null;
                        var kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind))
                        {
                            continue;
                        }

                        var labelKey = item.TryGetProperty("labelKey", out var lk) && lk.ValueKind == JsonValueKind.String ? lk.GetString() : null;
                        var hostAction = item.TryGetProperty("hostAction", out var ha) && ha.ValueKind == JsonValueKind.String ? ha.GetString() : null;
                        JsonElement? extra = null;
                        if (item.TryGetProperty("extraParameters", out var ep) && ep.ValueKind == JsonValueKind.Object)
                        {
                            extra = ep.Clone();
                        }

                        actions.Add(new PluginUiAction(id!, labelKey, kind!, hostAction, extra));
                    }
                }

                return new PluginUiSchema(titleKey, fields, actions);
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed record PluginUiField(
        string Name,
        string Control,
        string? LabelKey,
        string? OptionsStatePath,
        string? DefaultStatePath,
        bool EnumFromSchema,
        bool Required);

    private sealed record PluginUiAction(
        string Id,
        string? LabelKey,
        string Kind,
        string? HostAction,
        JsonElement? ExtraParameters);

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

                    fields.Add(new PluginUiField(prop.Name, control, LabelKey: null, OptionsStatePath: null, DefaultStatePath: null, EnumFromSchema: true, Required: false));
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
