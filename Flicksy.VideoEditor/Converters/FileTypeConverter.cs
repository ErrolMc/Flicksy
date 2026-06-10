using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Flicksy.VideoEditor.Converters;

/// <summary>
/// Extracts an uppercase extension ("MP4", "MKV") from a file path for the media bin's
/// details pane. Empty/extension-less paths render as an em dash.
/// </summary>
public sealed class FileTypeConverter : IValueConverter
{
    public static readonly FileTypeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return "—";

        string extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return "—";

        return extension.TrimStart('.').ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
