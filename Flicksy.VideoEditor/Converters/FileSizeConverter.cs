using System;
using System.Globalization;
using System.Windows.Data;

namespace Flicksy.VideoEditor.Converters;

/// <summary>
/// Formats a byte count (<see cref="long"/>) for the media bin's details pane, e.g.
/// "734 KB" / "12.4 MB". Zero, negative, and non-long inputs render as an em dash —
/// stub sources and stream-opened files have no known size.
/// </summary>
public sealed class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes || bytes <= 0)
            return "—";

        if (bytes < 1024)
            return $"{bytes} B";

        double kb = bytes / 1024.0;
        if (kb < 1024)
            return $"{kb:0.#} KB";

        double mb = kb / 1024.0;
        if (mb < 1024)
            return $"{mb:0.#} MB";

        return $"{mb / 1024.0:0.##} GB";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
