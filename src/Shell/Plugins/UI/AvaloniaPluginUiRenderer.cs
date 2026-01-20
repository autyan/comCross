using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ComCross.PluginSdk.UI;
using ComCross.Shared.Services;

namespace ComCross.Shell.Plugins.UI;

public class AvaloniaPluginUiRenderer : PluginUiRenderer
{
    private readonly ILocalizationService _localization;

    public AvaloniaPluginUiRenderer(
        IPluginUiControlFactory factory,
        PluginUiStateManager stateManager,
        PluginActionExecutor actionExecutor,
        ILocalizationService localization)
        : base(factory, stateManager, actionExecutor)
    {
        _localization = localization;
    }

    protected override IPluginUiContainer CreateNewContainer()
    {
        return new AvaloniaPluginUiContainer();
    }

    protected override void RenderToContainer(IPluginUiContainer container, string pluginId, string capabilityId, PluginUiSchema schema, string? sessionId, string? viewId)
    {
        if (schema.Layout is null)
        {
            base.RenderToContainer(container, pluginId, capabilityId, schema, sessionId, viewId);
            return;
        }

        container.Clear();

        var controls = BuildControls(pluginId, capabilityId, schema, sessionId, viewId);

        if (container is not AvaloniaPluginUiContainer avaloniaContainer)
        {
            // Unknown container implementation; fall back to linear rendering.
            foreach (var field in schema.Fields)
            {
                if (controls.TryGetValue(field.Key, out var ctrl))
                {
                    container.AddControl(field.Key, ctrl);
                }
            }
            foreach (var action in schema.Actions)
            {
                if (controls.TryGetValue(action.Id, out var actionCtrl))
                {
                    container.AddControl(action.Id, actionCtrl);
                }
            }
            return;
        }

        var fieldByKey = new Dictionary<string, PluginUiField>(StringComparer.Ordinal);
        foreach (var f in schema.Fields)
        {
            if (!string.IsNullOrWhiteSpace(f.Key))
            {
                fieldByKey[f.Key] = f;
            }
        }

        var layoutRoot = RenderNode(schema.Layout, fieldByKey, controls);
        avaloniaContainer.AddElement(layoutRoot);

        // For now: actions render as a bottom row (until we add explicit action layout nodes).
        if (schema.Actions.Count > 0)
        {
            var actionsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var action in schema.Actions)
            {
                if (!controls.TryGetValue(action.Id, out var actionControl))
                {
                    continue;
                }

                if (actionControl is AvaloniaPluginUiControl a)
                {
                    actionsRow.Children.Add(a.AvaloniaControl);
                }
            }

            if (actionsRow.Children.Count > 0)
            {
                avaloniaContainer.AddElement(actionsRow);
            }
        }
    }

    private Control RenderNode(
        PluginUiLayoutNode node,
        IReadOnlyDictionary<string, PluginUiField> fields,
        IDictionary<string, IPluginUiControl> controls)
    {
        switch (node)
        {
            case PluginUiLayoutStack stack:
            {
                var panel = new StackPanel
                {
                    Orientation = stack.Orientation == PluginUiStackOrientation.Horizontal
                        ? Orientation.Horizontal
                        : Orientation.Vertical,
                    Spacing = stack.Spacing,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Horizontal stacks should isolate their label scopes so they don't
                // force all valid vertical labels to expand unnecessarily.
                if (stack.Orientation == PluginUiStackOrientation.Horizontal)
                {
                    Grid.SetIsSharedSizeScope(panel, true);
                }

                foreach (var child in stack.Children)
                {
                    panel.Children.Add(RenderNode(child, fields, controls));
                }

                return panel;
            }

            case PluginUiLayoutFlow flow:
            {
                var panel = new WrapPanel
                {
                    Orientation = flow.Orientation == PluginUiStackOrientation.Vertical
                        ? Orientation.Vertical
                        : Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Flow items (wrapping) should definitely isolate scope, otherwise
                // items in a flow will be forced to match the width of the widest
                // label in the entire global form.
                Grid.SetIsSharedSizeScope(panel, true);

                foreach (var child in flow.Children)
                {
                    var element = RenderNode(child, fields, controls);

                    if (flow.MinItemWidth > 0)
                    {
                        element.MinWidth = flow.MinItemWidth;
                    }

                    element.Margin = new Thickness(Math.Max(0, flow.Gap / 2.0));
                    panel.Children.Add(element);
                }

                return panel;
            }

            case PluginUiLayoutGroup group:
            {
                var header = ResolveText(group.TitleKey, group.Title);
                var inner = new StackPanel { Spacing = 10 };
                foreach (var child in group.Children)
                {
                    inner.Children.Add(RenderNode(child, fields, controls));
                }

                var panel = new StackPanel { Spacing = 8 };
                if (!string.IsNullOrWhiteSpace(header))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = header,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.LightGray
                    });
                }
                panel.Children.Add(inner);

                return new Border
                {
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.DimGray,
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 6),
                    Child = panel
                };
            }

            case PluginUiLayoutGrid grid:
            {
                var g = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Grids usually imply a specific local layout structure, so we isolate scope.
                Grid.SetIsSharedSizeScope(g, true);

                // Columns as star weights
                foreach (var c in grid.Columns)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(1, c), GridUnitType.Star)));
                }

                var maxRow = 0;
                foreach (var item in grid.Items)
                {
                    maxRow = Math.Max(maxRow, item.Row + Math.Max(1, item.RowSpan));
                }
                for (var i = 0; i < Math.Max(1, maxRow); i++)
                {
                    g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }

                foreach (var item in grid.Items)
                {
                    if (item.Child is null)
                    {
                        continue;
                    }

                    var child = RenderNode(item.Child, fields, controls);
                    
                    // Smart margins: avoid outer margins to align with other container elements
                    var halfGap = Math.Max(0, grid.Gap / 2.0);
                    var marginLeft = item.Column == 0 ? 0 : halfGap;
                    var marginTop = item.Row == 0 ? 0 : halfGap;
                    var marginRight = (item.Column + item.ColumnSpan >= grid.Columns.Count) ? 0 : halfGap;
                    var marginBottom = (item.Row + item.RowSpan >= maxRow) ? 0 : halfGap;
                    
                    child.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
                    
                    Grid.SetRow(child, item.Row);
                    Grid.SetColumn(child, item.Column);
                    Grid.SetRowSpan(child, Math.Max(1, item.RowSpan));
                    Grid.SetColumnSpan(child, Math.Max(1, item.ColumnSpan));
                    g.Children.Add(child);
                }

                return g;
            }

            case PluginUiLayoutFieldRef fieldRef:
            {
                if (!controls.TryGetValue(fieldRef.Key, out var control) || control is not AvaloniaPluginUiControl avaloniaControl)
                {
                    return new TextBlock
                    {
                        Text = $"[Missing field: {fieldRef.Key}]",
                        Foreground = Brushes.OrangeRed
                    };
                }

                fields.TryGetValue(fieldRef.Key, out var field);
                var labelText = ResolveText(field?.LabelKey, field?.Label);

                // Checkbox already includes its own label.
                var kind = (field?.Control ?? field?.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (string.Equals(kind, "checkbox", StringComparison.Ordinal))
                {
                    avaloniaControl.AvaloniaControl.HorizontalAlignment = HorizontalAlignment.Stretch;
                    return avaloniaControl.AvaloniaControl;
                }

                var input = avaloniaControl.AvaloniaControl;
                input.HorizontalAlignment = HorizontalAlignment.Stretch;

                if (fieldRef.LabelPlacement == PluginUiLabelPlacement.Hidden)
                {
                    return input;
                }

                if (fieldRef.LabelPlacement == PluginUiLabelPlacement.Top)
                {
                    var panel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    panel.Children.Add(new TextBlock
                    {
                        Text = labelText,
                        Foreground = Brushes.LightGray,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    panel.Children.Add(input);
                    return panel;
                }

                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions
                    {
                        // Auto label column avoids clipping (especially in narrow sidebars).
                        new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "PluginFieldLabel" },
                        new ColumnDefinition(new GridLength(1, GridUnitType.Star))
                    },
                    RowDefinitions = new RowDefinitions("Auto"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                var label = new TextBlock
                {
                    Text = labelText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.LightGray,
                    FontSize = 11,
                    MaxWidth = 160,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(label, 0);
                Grid.SetColumn(input, 1);

                row.Children.Add(label);
                row.Children.Add(input);
                return row;
            }

            case PluginUiLayoutLabel label:
                return new TextBlock
                {
                    Text = ResolveText(label.TextKey, label.Text),
                    Foreground = Brushes.LightGray
                };

            case PluginUiLayoutSeparator:
                return new Separator
                {
                    Margin = new Thickness(0, 6)
                };

            default:
                return new TextBlock
                {
                    Text = "[Unsupported layout node]",
                    Foreground = Brushes.OrangeRed
                };
        }
    }

    private string ResolveText(string? key, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            return _localization.Strings[key];
        }

        return fallback ?? string.Empty;
    }
}
