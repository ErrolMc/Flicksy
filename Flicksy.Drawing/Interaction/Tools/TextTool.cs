using System;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Helpers;
using Flicksy.Drawing.Interaction.Config;
using Flicksy.Drawing.Source;
using Flicksy.Drawing.ViewModels;

namespace Flicksy.Drawing.Interaction.Tools;

/// <summary>
/// Click-to-create / click-to-edit gesture for text items. If the click lands on an existing
/// <see cref="TextItem"/>, opens the in-place editor on that item; otherwise creates a new
/// text item at the click point (using the configured font/fill/outline) and immediately
/// opens it for editing. The in-place TextBox overlay itself is managed by
/// <c>TextEditingController</c>, which reacts to <see cref="DrawingViewModel.EditingTextItem"/>
/// changes — this tool just triggers them.
///
/// <para>
/// No drag/scale/stroke state — <see cref="IsActive"/> is always false. The mouse is never
/// captured because the editor takes over input as soon as the overlay shows.
/// </para>
/// </summary>
public sealed class TextTool : IDrawingTool
{
    private readonly DrawingViewModel _viewModel;
    private readonly ITextConfig _config;

    public TextTool(DrawingViewModel viewModel, ITextConfig config)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public bool IsActive => false;

    public bool OnPointerDown(Point point, MouseButtonEventArgs e)
    {
        if (DrawingMath.HitTestTopmost(_viewModel.Items, point) is TextItem existing)
        {
            _viewModel.BeginEditText(existing);
        }
        else
        {
            TextItem created = _viewModel.BeginText(
                point,
                _config.TextFontFamily,
                _config.TextFontSize,
                _config.TextFillBrush,
                _config.TextOutlineBrush,
                _config.TextOutlineThickness);
            _viewModel.BeginEditText(created);
        }

        return true;
    }

    public void OnPointerMove(Point point, MouseEventArgs e)
    {
        // Text tool is click-only; the in-place editor handles subsequent input.
    }

    public void OnPointerUp(Point point, MouseButtonEventArgs e)
    {
        // Text tool is click-only; nothing to release.
    }

    public void OnPointerHover(Point point, MouseEventArgs e)
    {
        // Text tool has no hover affordance.
    }
}
