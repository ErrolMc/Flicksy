using System;
using System.Globalization;
using System.Windows.Data;

namespace Flicksy.VideoEditor.Converters;

/// <summary>
/// Formats a bitrate in bits/s (<see cref="long"/>, FFmpeg's container bit_rate) for the
/// media bin's details pane, e.g. "8.2 Mbps" / "192 kbps". Zero ("unknown" per FFmpeg)
/// and non-long inputs render as an em dash.
/// </summary>
public sealed class BitrateConverter : IValueConverter
{
    public static readonly BitrateConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bitsPerSecond || bitsPerSecond <= 0)
            return "—";

        if (bitsPerSecond >= 1_000_000)
            return $"{bitsPerSecond / 1_000_000.0:0.#} Mbps";

        if (bitsPerSecond >= 1_000)
            return $"{bitsPerSecond / 1_000.0:0.#} kbps";

        return $"{bitsPerSecond} bps";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
