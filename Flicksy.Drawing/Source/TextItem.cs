using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Flicksy.Drawing.Source;

public sealed class TextItem : DrawingItem
{
    private string _text;
    private string _fontFamily;
    private double _fontSize;
    private Brush? _fill;
    private Brush? _outline;
    private double _outlineThickness;
    private Point _origin;
    private bool _isEditing;
    private Rect _glyphBounds = Rect.Empty;

    public TextItem(Point origin, string fontFamily, double fontSize, Brush? fill, Brush? outline, double outlineThickness, string text = "")
    {
        _origin = origin;
        _fontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily;
        _fontSize = Math.Max(1d, fontSize);
        _fill = fill;
        _outline = outline;
        _outlineThickness = Math.Max(0d, outlineThickness);
        _text = text ?? string.Empty;
        RebuildGeometry();
    }

    public string Text
    {
        get => _text;
        private set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                RebuildGeometry();
            }
        }
    }

    public string FontFamily
    {
        get => _fontFamily;
        private set
        {
            if (SetProperty(ref _fontFamily, value))
            {
                RebuildGeometry();
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        private set
        {
            if (SetProperty(ref _fontSize, value))
            {
                RebuildGeometry();
            }
        }
    }

    public Brush? Fill
    {
        get => _fill;
        private set => SetProperty(ref _fill, value);
    }

    public Brush? Outline
    {
        get => _outline;
        private set => SetProperty(ref _outline, value);
    }

    public double OutlineThickness
    {
        get => _outlineThickness;
        private set
        {
            if (SetProperty(ref _outlineThickness, value))
            {
                OnPropertyChanged(nameof(Geometry));
            }
        }
    }

    public Point Origin
    {
        get => _origin;
        private set
        {
            if (SetProperty(ref _origin, value))
            {
                RebuildGeometry();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public bool IsEmpty => string.IsNullOrEmpty(_text);

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void SetFontFamily(string fontFamily)
    {
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            FontFamily = fontFamily;
        }
    }

    public void SetFontSize(double fontSize)
    {
        FontSize = Math.Max(1d, fontSize);
    }

    public void SetFill(Brush? fill)
    {
        Fill = fill;
    }

    public void SetOutline(Brush? outline, double thickness)
    {
        Outline = outline;
        OutlineThickness = Math.Max(0d, thickness);
    }

    public override DrawingItem Clone()
    {
        // IsEditing is transient edit-session state and is intentionally not carried over.
        var clone = new TextItem(Origin, FontFamily, FontSize, Fill, Outline, OutlineThickness, Text);
        clone.Transform.Matrix = Transform.Matrix;
        return clone;
    }

    public override Rect CanonicalBounds
    {
        get
        {
            // While the text is empty (e.g. just starting a new text item) report empty bounds
            // so the selection overlay stays hidden until the user actually types something.
            if (IsEmpty || _glyphBounds.IsEmpty)
            {
                return Rect.Empty;
            }

            Rect b = _glyphBounds;
            double inflate = Outline is not null ? OutlineThickness / 2.0 : 0d;
            if (inflate > 0)
            {
                b.Inflate(inflate, inflate);
            }
            return b;
        }
    }

    public override bool HitTest(Point localPoint)
    {
        if (CanonicalBounds.Contains(localPoint))
        {
            return true;
        }

        if (Geometry == Geometry.Empty)
        {
            return false;
        }

        if (Fill is not null && Geometry.FillContains(localPoint))
        {
            return true;
        }

        if (Outline is not null)
        {
            var pen = new Pen(Outline, Math.Max(OutlineThickness, 6d));
            if (Geometry.StrokeContains(pen, localPoint))
            {
                return true;
            }
        }

        return false;
    }

    public override void Render(DrawingContext dc)
    {
        if (Geometry == Geometry.Empty)
        {
            return;
        }

        Pen? pen = null;
        if (Outline is not null && OutlineThickness > 0)
        {
            pen = new Pen(Outline, OutlineThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
        }

        dc.PushTransform(Transform);
        dc.DrawGeometry(Fill, pen, Geometry);
        dc.Pop();
    }

    private void RebuildGeometry()
    {
        if (string.IsNullOrEmpty(_text))
        {
            Geometry = Geometry.Empty;
            _glyphBounds = Rect.Empty;
            OnPropertyChanged(nameof(CanonicalBounds));
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        var typeface = new Typeface(new FontFamily(_fontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(
            _text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _fontSize,
            Brushes.Black,
            1.0);

        Geometry geometry = formatted.BuildGeometry(_origin);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        _glyphBounds = geometry.Bounds;
        Geometry = geometry;
        OnPropertyChanged(nameof(CanonicalBounds));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private Rect EmptyCaretBounds()
    {
        // Caret-sized box at the origin so the selection overlay and hit-testing remain usable when empty.
        double width = Math.Max(_fontSize * 0.5, 6d);
        double height = Math.Max(_fontSize, 6d);
        return new Rect(_origin.X, _origin.Y, width, height);
    }
}
