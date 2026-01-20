using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComCross.PluginSdk.UI;

/// <summary>
/// 插件 UI 字段定义
/// </summary>
public class PluginUiField
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; // 兼容旧代码使用 Name
    public string Label { get; set; } = string.Empty; // 显示名称
    public string LabelKey { get; set; } = string.Empty; // 多语言 Key
    public string Type { get; set; } = "text"; // text, select, number, checkbox
    public string Control { get; set; } = "text"; // 兼容旧代码使用 Control
    /// <summary>
    /// Options definition.
    /// 
    /// Supports legacy format: ["a", "b"]
    /// Supports v1 format: [{"value":"a","labelKey":"..."}, ...]
    /// </summary>
    public JsonElement? Options { get; set; }
    public object? DefaultValue { get; set; }
    public string? DefaultStatePath { get; set; } // 从 UiState 中获取默认值的路径
    public string? OptionsStatePath { get; set; } // 从 UiState 中获取选项列表的路径
    public bool EnumFromSchema { get; set; } // 是否从 JsonSchema 中获取枚举

    // v1 extensions (optional)
    public string? Help { get; set; }
    public string? HelpKey { get; set; }
    public string? Placeholder { get; set; }
    public string? PlaceholderKey { get; set; }
    public bool Required { get; set; }
    public bool ReadOnly { get; set; }
    public bool Editable { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }

    public IReadOnlyList<PluginUiOption> GetOptionsAsOptionList()
    {
        if (Options is null)
        {
            return new List<PluginUiOption>();
        }

        if (Options.Value.ValueKind != JsonValueKind.Array)
        {
            return new List<PluginUiOption>();
        }

        var result = new List<PluginUiOption>();
        foreach (var item in Options.Value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var v = item.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(v))
                {
                    result.Add(new PluginUiOption(v, Label: v));
                }
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object)
            {
                var value = TryGetString(item, "value") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var labelKey = TryGetString(item, "labelKey");
                var label = TryGetString(item, "label");
                result.Add(new PluginUiOption(value, labelKey, label));
                continue;
            }
        }

        return result;
    }

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, propertyName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }
}

/// <summary>
/// 插件 UI 整体架构定义
/// </summary>
public class PluginUiSchema
{
    [JsonPropertyName("uiSchemaVersion")]
    public int UiSchemaVersion { get; set; } = 1;

    public string Title { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public List<PluginUiField> Fields { get; set; } = new();
    public List<PluginUiAction> Actions { get; set; } = new();

    public PluginUiLayoutNode? Layout { get; set; }

    public static PluginUiSchema? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var schema = JsonSerializer.Deserialize<PluginUiSchema>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                });

            if (schema is null)
            {
                return null;
            }

            // Normalize fields for forward/backward compatibility:
            // - Prefer Key for state binding; fall back to Name.
            // - Prefer Control for rendering; fall back to Type.
            foreach (var field in schema.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key) && !string.IsNullOrWhiteSpace(field.Name))
                {
                    field.Key = field.Name;
                }
                else if (string.IsNullOrWhiteSpace(field.Name) && !string.IsNullOrWhiteSpace(field.Key))
                {
                    field.Name = field.Key;
                }

                if (string.IsNullOrWhiteSpace(field.Control) && !string.IsNullOrWhiteSpace(field.Type))
                {
                    field.Control = field.Type;
                }
                else if (string.IsNullOrWhiteSpace(field.Type) && !string.IsNullOrWhiteSpace(field.Control))
                {
                    field.Type = field.Control;
                }

                // v1 defaults
                if (schema.UiSchemaVersion <= 0)
                {
                    schema.UiSchemaVersion = 1;
                }
            }

            // Normalize actions:
            foreach (var action in schema.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Id) && !string.IsNullOrWhiteSpace(action.Name))
                {
                    action.Id = action.Name;
                }
                else if (string.IsNullOrWhiteSpace(action.Name) && !string.IsNullOrWhiteSpace(action.Id))
                {
                    action.Name = action.Id;
                }
            }

            return schema;
        }
        catch { return null; }
    }
}

/// <summary>
/// 插件 UI 动作定义
/// </summary>
public class PluginUiAction
{
    public string Id { get; set; } = string.Empty; // 兼容旧代码使用 Id
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty; // 多语言 Key
    public string Kind { get; set; } = "plugin"; // plugin, host
    public string? HostAction { get; set; }
    public JsonElement? ExtraParameters { get; set; }
}

public sealed record PluginUiOption(
    string Value,
    string? LabelKey = null,
    string? Label = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PluginUiLayoutStack), "stack")]
[JsonDerivedType(typeof(PluginUiLayoutFlow), "flow")]
[JsonDerivedType(typeof(PluginUiLayoutGrid), "grid")]
[JsonDerivedType(typeof(PluginUiLayoutGroup), "group")]
[JsonDerivedType(typeof(PluginUiLayoutFieldRef), "field")]
[JsonDerivedType(typeof(PluginUiLayoutLabel), "label")]
[JsonDerivedType(typeof(PluginUiLayoutSeparator), "separator")]
public abstract class PluginUiLayoutNode
{
}

public enum PluginUiStackOrientation
{
    Vertical,
    Horizontal
}

public sealed class PluginUiLayoutStack : PluginUiLayoutNode
{
    public PluginUiStackOrientation Orientation { get; set; } = PluginUiStackOrientation.Vertical;
    public int Spacing { get; set; } = 8;
    public List<PluginUiLayoutNode> Children { get; set; } = new();
}

/// <summary>
/// Flow layout: renders children in a wrapping flow (responsive) container.
/// Useful for narrow sidebars vs wide dialogs.
/// </summary>
public sealed class PluginUiLayoutFlow : PluginUiLayoutNode
{
    public PluginUiStackOrientation Orientation { get; set; } = PluginUiStackOrientation.Horizontal;

    /// <summary>
    /// Gap between items (pixels).
    /// </summary>
    public int Gap { get; set; } = 8;

    /// <summary>
    /// Minimum width for each item (pixels). Set to 0 to disable.
    /// </summary>
    public int MinItemWidth { get; set; } = 260;

    public List<PluginUiLayoutNode> Children { get; set; } = new();
}

public sealed class PluginUiLayoutGroup : PluginUiLayoutNode
{
    public string? TitleKey { get; set; }
    public string? Title { get; set; }
    public List<PluginUiLayoutNode> Children { get; set; } = new();
}

public sealed class PluginUiLayoutGrid : PluginUiLayoutNode
{
    public int TotalWidth { get; set; } = 10;
    public List<int> Columns { get; set; } = new() { 10 };
    public int Gap { get; set; } = 8;
    public List<PluginUiLayoutGridItem> Items { get; set; } = new();
}

public sealed class PluginUiLayoutGridItem
{
    public PluginUiLayoutNode? Child { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
}

public sealed class PluginUiLayoutFieldRef : PluginUiLayoutNode
{
    public string Key { get; set; } = string.Empty;

    public PluginUiLabelPlacement LabelPlacement { get; set; } = PluginUiLabelPlacement.Left;

    // Ratio-based widths for this field row.
    public int LabelWidth { get; set; } = 3;
    public int TotalWidth { get; set; } = 10;
}

public enum PluginUiLabelPlacement
{
    Left,
    Top,
    Hidden
}

public sealed class PluginUiLayoutLabel : PluginUiLayoutNode
{
    public string? TextKey { get; set; }
    public string? Text { get; set; }
}

public sealed class PluginUiLayoutSeparator : PluginUiLayoutNode
{
}
