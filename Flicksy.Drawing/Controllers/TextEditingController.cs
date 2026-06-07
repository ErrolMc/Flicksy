using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Flicksy.Drawing.Controls;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Controllers;

/// <summary>
/// Manages the in-place text editor (overlay <see cref="TextBox"/>) for a <see cref="DrawingView"/>:
/// shows/hides it, positions it over the editing <see cref="TextItem"/>, keeps content in sync,
/// and handles focus/keyboard commit semantics.
/// </summary>
internal sealed class TextEditingController
{
    private readonly TextBox _editTextBox;
    private readonly Func<DrawingViewModel?> _getViewModel;

    private DrawingViewModel? _subscribedViewModel;
    private TextItem? _editingItem;
    private bool _suppressEditTextSync;

    public TextEditingController(TextBox editTextBox, Func<DrawingViewModel?> getViewModel)
    {
        _editTextBox = editTextBox;
        _getViewModel = getViewModel;

        _editTextBox.TextChanged += OnEditTextBoxTextChanged;
        _editTextBox.LostKeyboardFocus += OnEditTextBoxLostFocus;
        _editTextBox.PreviewKeyDown += OnEditTextBoxPreviewKeyDown;
    }

    public void OnHostDataContextChanged(DrawingViewModel? oldValue, DrawingViewModel? newValue)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (newValue is not null)
        {
            _subscribedViewModel = newValue;
            newValue.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyEditingTextItem(_getViewModel()?.EditingTextItem);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DrawingViewModel.EditingTextItem))
        {
            ApplyEditingTextItem(_getViewModel()?.EditingTextItem);
        }
    }

    private void ApplyEditingTextItem(TextItem? item)
    {
        if (ReferenceEquals(_editingItem, item))
        {
            return;
        }

        if (_editingItem is not null)
        {
            _editingItem.PropertyChanged -= OnEditingItemPropertyChanged;
            _editingItem.Transform.Changed -= OnEditingItemTransformChanged;
        }

        _editingItem = item;

        if (item is null)
        {
            _editTextBox.Visibility = Visibility.Collapsed;
            _editTextBox.RenderTransform = Transform.Identity;
            _suppressEditTextSync = true;
            _editTextBox.Text = string.Empty;
            _suppressEditTextSync = false;
            return;
        }

        item.PropertyChanged += OnEditingItemPropertyChanged;
        item.Transform.Changed += OnEditingItemTransformChanged;

        ConfigureEditTextBoxForItem(item);

        _suppressEditTextSync = true;
        _editTextBox.Text = item.Text;
        _suppressEditTextSync = false;

        _editTextBox.Visibility = Visibility.Visible;
        PositionEditTextBox(item);

        _editTextBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_editingItem, item))
            {
                return;
            }

            Keyboard.Focus(_editTextBox);
            _editTextBox.Focus();
            _editTextBox.CaretIndex = _editTextBox.Text.Length;
            _editTextBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void ConfigureEditTextBoxForItem(TextItem item)
    {
        _editTextBox.FontFamily = new FontFamily(item.FontFamily);
        _editTextBox.FontSize = item.FontSize;
        // Keep the textbox content invisible while editing so the live-rendered geometry
        // (with fill AND outline) shows through; the caret remains visible via CaretBrush.
        _editTextBox.Foreground = Brushes.Transparent;
        _editTextBox.CaretBrush = item.Fill ?? item.Outline ?? Brushes.Black;
    }

    private void PositionEditTextBox(TextItem item)
    {
        Matrix matrix = item.Transform.Matrix;

        // Align the textbox's first-line baseline with the geometry's baseline.
        // Geometry baseline in canonical Y = item.Origin.Y + FormattedText.Baseline.
        // TextBox first-line baseline in textbox-local Y = MeasureFirstLineBaseline(...).
        // Canvas position is in DrawingView coords; the item's linear (rotation/scale) part
        // is applied as RenderTransform around the textbox's top-left.
        double formattedBaseline = GetFormattedTextBaseline(item.FontFamily, item.FontSize);
        double textBoxBaseline = MeasureFirstLineBaseline(item.FontFamily, item.FontSize);
        double baselineDelta = formattedBaseline - textBoxBaseline;

        Point worldOrigin = matrix.Transform(item.Origin);
        double offsetX = matrix.M21 * baselineDelta;
        double offsetY = matrix.M22 * baselineDelta;

        Canvas.SetLeft(_editTextBox, worldOrigin.X + offsetX);
        Canvas.SetTop(_editTextBox, worldOrigin.Y + offsetY);

        // Give the textbox a sensible minimum size so the caret stays visible when empty.
        _editTextBox.MinWidth = Math.Max(item.CanonicalBounds.Width, item.FontSize * 0.5);
        _editTextBox.MinHeight = Math.Max(item.CanonicalBounds.Height, item.FontSize * 1.2);

        var rotationScale = new Matrix(matrix.M11, matrix.M12, matrix.M21, matrix.M22, 0, 0);
        _editTextBox.RenderTransformOrigin = new Point(0, 0);
        _editTextBox.RenderTransform = rotationScale.IsIdentity
            ? Transform.Identity
            : new MatrixTransform(rotationScale);
    }

    private static double GetFormattedTextBaseline(string fontFamilyName, double fontSize)
    {
        var typeface = new Typeface(
            new FontFamily(fontFamilyName),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var formatted = new FormattedText(
            "Hg",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0);
        return formatted.Baseline;
    }

    private static double MeasureFirstLineBaseline(string fontFamilyName, double fontSize)
    {
        var probe = new TextBlock
        {
            Text = "Hg",
            FontFamily = new FontFamily(fontFamilyName),
            FontSize = fontSize,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.BaselineOffset;
    }

    private void OnEditingItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_editingItem is null)
        {
            return;
        }

        if (e.PropertyName is nameof(TextItem.Text)
            or nameof(TextItem.FontFamily)
            or nameof(TextItem.FontSize)
            or nameof(TextItem.Fill)
            or nameof(TextItem.Outline)
            or nameof(TextItem.OutlineThickness)
            or nameof(TextItem.CanonicalBounds)
            or nameof(DrawingItem.Geometry))
        {
            ConfigureEditTextBoxForItem(_editingItem);
            PositionEditTextBox(_editingItem);
        }
    }

    private void OnEditingItemTransformChanged(object? sender, EventArgs e)
    {
        if (_editingItem is not null)
        {
            PositionEditTextBox(_editingItem);
        }
    }

    private void OnEditTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditTextSync || _editingItem is null)
        {
            return;
        }

        _editingItem.SetText(_editTextBox.Text);
    }

    private void OnEditTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Keep the editor alive when focus moves into the tools panel or any of its popups,
        // so the user can change font/size/fill/outline of the editing text without losing it.
        if (e.NewFocus is DependencyObject focusTarget && IsWithinImageEditTools(focusTarget))
        {
            return;
        }

        _getViewModel()?.EndEditText(commit: true);
    }

    private void OnEditTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _getViewModel()?.EndEditText(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            _getViewModel()?.EndEditText(commit: true);
            e.Handled = true;
        }
    }

    private static bool IsWithinImageEditTools(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (TextEditingHost.GetKeepTextEditorAlive(current))
            {
                return true;
            }

            // Walk up the logical tree first (handles popup content), then fall back to visual tree.
            DependencyObject next = LogicalTreeHelper.GetParent(current);
            if (next is null && current is Visual visual)
            {
                next = VisualTreeHelper.GetParent(visual);
            }
            current = next;
        }
        return false;
    }
}
