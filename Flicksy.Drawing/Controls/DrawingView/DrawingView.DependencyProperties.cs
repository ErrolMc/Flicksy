using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Controls;

public partial class DrawingView : UserControl
{
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(
            nameof(StrokeBrush),
            typeof(Brush),
            typeof(DrawingView),
            new PropertyMetadata(Brushes.Black, OnSelectedPenStyleChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(DrawingView),
            new PropertyMetadata(1.0, OnSelectedPenStyleChanged));

    public static readonly DependencyProperty IsErasingProperty =
        DependencyProperty.Register(
            nameof(IsErasing),
            typeof(bool),
            typeof(DrawingView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectActiveProperty =
        DependencyProperty.Register(
            nameof(IsSelectActive),
            typeof(bool),
            typeof(DrawingView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsShapeActiveProperty =
        DependencyProperty.Register(
            nameof(IsShapeActive),
            typeof(bool),
            typeof(DrawingView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsTextActiveProperty =
        DependencyProperty.Register(
            nameof(IsTextActive),
            typeof(bool),
            typeof(DrawingView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TextFontFamilyProperty =
        DependencyProperty.Register(
            nameof(TextFontFamily),
            typeof(string),
            typeof(DrawingView),
            new PropertyMetadata("Segoe UI", OnSelectedTextStyleChanged));

    public static readonly DependencyProperty TextFontSizeProperty =
        DependencyProperty.Register(
            nameof(TextFontSize),
            typeof(double),
            typeof(DrawingView),
            new PropertyMetadata(24.0, OnSelectedTextStyleChanged));

    public static readonly DependencyProperty TextFillBrushProperty =
        DependencyProperty.Register(
            nameof(TextFillBrush),
            typeof(Brush),
            typeof(DrawingView),
            new PropertyMetadata(null, OnSelectedTextStyleChanged));

    public static readonly DependencyProperty TextOutlineBrushProperty =
        DependencyProperty.Register(
            nameof(TextOutlineBrush),
            typeof(Brush),
            typeof(DrawingView),
            new PropertyMetadata(null, OnSelectedTextStyleChanged));

    public static readonly DependencyProperty TextOutlineThicknessProperty =
        DependencyProperty.Register(
            nameof(TextOutlineThickness),
            typeof(double),
            typeof(DrawingView),
            new PropertyMetadata(4.0, OnSelectedTextStyleChanged));

    public static readonly DependencyProperty ActiveShapeProperty =
        DependencyProperty.Register(
            nameof(ActiveShape),
            typeof(ShapeKind),
            typeof(DrawingView),
            new PropertyMetadata(ShapeKind.Square));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(DrawingView),
            new PropertyMetadata(null, OnSelectedShapeStyleChanged));

    public static readonly DependencyProperty OutlineBrushProperty =
        DependencyProperty.Register(
            nameof(OutlineBrush),
            typeof(Brush),
            typeof(DrawingView),
            new PropertyMetadata(null, OnSelectedShapeStyleChanged));

    public static readonly DependencyProperty OutlineThicknessProperty =
        DependencyProperty.Register(
            nameof(OutlineThickness),
            typeof(double),
            typeof(DrawingView),
            new PropertyMetadata(4.0, OnSelectedShapeStyleChanged));

    public static readonly DependencyProperty ContentScaleProperty =
        DependencyProperty.Register(
            nameof(ContentScale),
            typeof(double),
            typeof(DrawingView),
            new PropertyMetadata(1.0));

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool IsErasing
    {
        get => (bool)GetValue(IsErasingProperty);
        set => SetValue(IsErasingProperty, value);
    }

    public bool IsSelectActive
    {
        get => (bool)GetValue(IsSelectActiveProperty);
        set => SetValue(IsSelectActiveProperty, value);
    }

    public bool IsShapeActive
    {
        get => (bool)GetValue(IsShapeActiveProperty);
        set => SetValue(IsShapeActiveProperty, value);
    }

    public bool IsTextActive
    {
        get => (bool)GetValue(IsTextActiveProperty);
        set => SetValue(IsTextActiveProperty, value);
    }

    public string TextFontFamily
    {
        get => (string)GetValue(TextFontFamilyProperty);
        set => SetValue(TextFontFamilyProperty, value);
    }

    public double TextFontSize
    {
        get => (double)GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    public Brush? TextFillBrush
    {
        get => (Brush?)GetValue(TextFillBrushProperty);
        set => SetValue(TextFillBrushProperty, value);
    }

    public Brush? TextOutlineBrush
    {
        get => (Brush?)GetValue(TextOutlineBrushProperty);
        set => SetValue(TextOutlineBrushProperty, value);
    }

    public double TextOutlineThickness
    {
        get => (double)GetValue(TextOutlineThicknessProperty);
        set => SetValue(TextOutlineThicknessProperty, value);
    }

    public ShapeKind ActiveShape
    {
        get => (ShapeKind)GetValue(ActiveShapeProperty);
        set => SetValue(ActiveShapeProperty, value);
    }

    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush? OutlineBrush
    {
        get => (Brush?)GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public double OutlineThickness
    {
        get => (double)GetValue(OutlineThicknessProperty);
        set => SetValue(OutlineThicknessProperty, value);
    }

    public double ContentScale
    {
        get => (double)GetValue(ContentScaleProperty);
        set => SetValue(ContentScaleProperty, value);
    }

    private static void OnSelectedTextStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DrawingView view || view.ViewModel?.SelectedItem is not TextItem text)
        {
            return;
        }

        if (e.Property == TextFontFamilyProperty && e.NewValue is string family)
        {
            text.SetFontFamily(family);
        }
        else if (e.Property == TextFontSizeProperty && e.NewValue is double size)
        {
            text.SetFontSize(size);
        }
        else if (e.Property == TextFillBrushProperty)
        {
            text.SetFill(e.NewValue as Brush);
        }
        else if (e.Property == TextOutlineBrushProperty)
        {
            text.SetOutline(e.NewValue as Brush, view.TextOutlineThickness);
        }
        else if (e.Property == TextOutlineThicknessProperty && e.NewValue is double thickness)
        {
            text.SetOutline(view.TextOutlineBrush, thickness);
        }
    }

    private static void OnSelectedPenStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DrawingView view || view.ViewModel?.SelectedItem is not PenStrokeItem pen)
        {
            return;
        }

        if (e.Property == StrokeBrushProperty && e.NewValue is Brush brush)
        {
            pen.SetStyle(brush, view.StrokeThickness);
        }
        else if (e.Property == StrokeThicknessProperty && e.NewValue is double thickness)
        {
            pen.SetStyle(view.StrokeBrush, thickness);
        }
    }

    private static void OnSelectedShapeStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DrawingView view || view.ViewModel?.SelectedItem is not ShapeItem shape)
        {
            return;
        }

        if (e.Property == FillBrushProperty)
        {
            shape.SetFill(e.NewValue as Brush);
        }
        else if (e.Property == OutlineBrushProperty)
        {
            shape.SetOutline(e.NewValue as Brush, view.OutlineThickness);
        }
        else if (e.Property == OutlineThicknessProperty && e.NewValue is double thickness)
        {
            shape.SetOutline(view.OutlineBrush, thickness);
        }
    }
}
