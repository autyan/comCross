using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;

namespace ComCross.Shell.Views;

internal sealed class SlimDirectionMargin : AbstractMargin
{
    private const double MarginWidth = 38;
    private readonly Dictionary<int, string> _labelsByLine = new();
    private IBrush _textBrush = Brushes.Gray;
    private IBrush _separatorBrush = Brushes.Gray;
    private FontFamily _fontFamily = FontFamily.Default;
    private double _fontSize = 13;

    public void SetLabels(string gutterText)
    {
        _labelsByLine.Clear();

        var lineNumber = 1;
        foreach (var line in gutterText.Split('\n'))
        {
            var label = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(label))
            {
                _labelsByLine[lineNumber] = label;
            }

            lineNumber++;
        }

        InvalidateVisual();
    }

    public void SetStyle(IBrush textBrush, IBrush separatorBrush, FontFamily fontFamily, double fontSize)
    {
        _textBrush = textBrush;
        _separatorBrush = separatorBrush;
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(MarginWidth, 0);

    public override void Render(DrawingContext drawingContext)
    {
        base.Render(drawingContext);

        var textView = TextView;
        if (textView?.Document is null)
        {
            return;
        }

        textView.EnsureVisualLines();
        drawingContext.DrawLine(
            new Pen(_separatorBrush, 1),
            new Point(Bounds.Width - 0.5, 0),
            new Point(Bounds.Width - 0.5, Bounds.Height));

        foreach (var visualLine in textView.VisualLines)
        {
            if (!_labelsByLine.TryGetValue(visualLine.FirstDocumentLine.LineNumber, out var label))
            {
                continue;
            }

            var text = new FormattedText(
                label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(_fontFamily),
                _fontSize,
                _textBrush);
            var y = visualLine.VisualTop - textView.VerticalOffset + Math.Max(0, (visualLine.Height - text.Height) / 2);
            drawingContext.DrawText(text, new Point(8, y));
        }
    }
}
