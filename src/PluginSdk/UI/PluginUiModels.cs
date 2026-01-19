using System.Collections.Generic;
using System.Text.Json;

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
    public List<string>? Options { get; set; } // Type 为 select 时使用
    public object? DefaultValue { get; set; }
    public string? DefaultStatePath { get; set; } // 从 UiState 中获取默认值的路径
    public string? OptionsStatePath { get; set; } // 从 UiState 中获取选项列表的路径
    public bool EnumFromSchema { get; set; } // 是否从 JsonSchema 中获取枚举
}

/// <summary>
/// 插件 UI 整体架构定义
/// </summary>
public class PluginUiSchema
{
    public string Title { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public List<PluginUiField> Fields { get; set; } = new();
    public List<PluginUiAction> Actions { get; set; } = new();

    public static PluginUiSchema? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PluginUiSchema>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
