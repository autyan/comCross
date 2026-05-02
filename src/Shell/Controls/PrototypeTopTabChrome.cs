using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ComCross.Shell.Controls;

public class PrototypeTopTabChrome : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<PrototypeTopTabChrome>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<PrototypeTopTabChrome>();

    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<PrototypeTopTabChrome>();

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<PrototypeTopTabChrome>();

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<PrototypeTopTabChrome, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> ActiveTopBrushProperty =
        AvaloniaProperty.Register<PrototypeTopTabChrome, IBrush?>(nameof(ActiveTopBrush));

    public static readonly StyledProperty<double> ActiveTopThicknessProperty =
        AvaloniaProperty.Register<PrototypeTopTabChrome, double>(nameof(ActiveTopThickness), 3d);

    static PrototypeTopTabChrome()
    {
        AffectsRender<PrototypeTopTabChrome>(
            IsActiveProperty,
            ActiveTopBrushProperty,
            ActiveTopThicknessProperty,
            BackgroundProperty,
            BorderBrushProperty,
            BorderThicknessProperty,
            CornerRadiusProperty);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public Thickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? ActiveTopBrush
    {
        get => GetValue(ActiveTopBrushProperty);
        set => SetValue(ActiveTopBrushProperty, value);
    }

    public double ActiveTopThickness
    {
        get => GetValue(ActiveTopThicknessProperty);
        set => SetValue(ActiveTopThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var outerRoundedRect = new RoundedRect(bounds, CornerRadius);
        if (BorderBrush is not null)
        {
            context.DrawRectangle(BorderBrush, null, outerRoundedRect);
        }

        if (IsActive && ActiveTopBrush is not null && ActiveTopThickness > 0)
        {
            using (context.PushClip(new Rect(0, 0, bounds.Width, Math.Min(bounds.Height, ActiveTopThickness))))
            {
                context.DrawRectangle(ActiveTopBrush, null, outerRoundedRect);
            }
        }

        if (Background is null)
        {
            return;
        }

        var thickness = BorderThickness;
        var topInset = IsActive ? Math.Max(thickness.Top, ActiveTopThickness) : thickness.Top;
        var innerRect = new Rect(
            thickness.Left,
            topInset,
            Math.Max(0, bounds.Width - thickness.Left - thickness.Right),
            Math.Max(0, bounds.Height - topInset - thickness.Bottom));

        if (innerRect.Width <= 0 || innerRect.Height <= 0)
        {
            return;
        }

        var innerCornerRadius = new CornerRadius(
            Math.Max(0, CornerRadius.TopLeft - thickness.Left),
            Math.Max(0, CornerRadius.TopRight - thickness.Right),
            Math.Max(0, CornerRadius.BottomRight - thickness.Right),
            Math.Max(0, CornerRadius.BottomLeft - thickness.Left));

        context.DrawRectangle(Background, null, new RoundedRect(innerRect, innerCornerRadius));
    }
}
