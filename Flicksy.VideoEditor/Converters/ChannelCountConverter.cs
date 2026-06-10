using System;
using System.Globalization;
using System.Windows.Data;

namespace Flicksy.VideoEditor.Converters;

/// <summary>
/// Formats an audio channel count for the media bin's details pane, appending the
/// conventional layout name for common counts: "1 (Mono)", "2 (Stereo)", "6 (5.1)",
/// "8 (7.1)". Other counts render as the bare number; zero/non-int as an em dash.
/// The name is derived from the count — FFMediaToolkit doesn't expose the stream's
/// actual channel layout publicly.
/// </summary>
public sealed class ChannelCountConverter : IValueConverter
{
    public static readonly ChannelCountConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int channels || channels <= 0)
            return "—";

        string? layoutName = channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            6 => "5.1",
            8 => "7.1",
            _ => null,
        };

        return layoutName is null ? channels.ToString(culture) : $"{channels} ({layoutName})";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
