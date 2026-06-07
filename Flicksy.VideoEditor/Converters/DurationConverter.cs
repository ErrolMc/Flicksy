using System;
using System.Globalization;
using System.Windows.Data;

namespace Flicksy.VideoEditor.Converters;

/// <summary>
/// Formats a <see cref="TimeSpan"/> for the media bin's duration badge.
/// Output is <c>m:ss</c> under an hour, <c>h:mm:ss</c> at or above. Non-TimeSpan inputs
/// and the zero-duration case render as empty string.
/// </summary>
public sealed class DurationConverter : IValueConverter
{
    public static readonly DurationConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan ts || ts <= TimeSpan.Zero) 
            return string.Empty;

        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
