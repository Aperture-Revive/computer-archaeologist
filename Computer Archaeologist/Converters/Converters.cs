using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ComputerArchaeologist.Converters;

/// <summary>Localization access for value converters, which cannot use constructor injection.</summary>
internal static class ConverterServices
{
    public static ILocalizationService? Localization =>
        App.Services?.GetService<ILocalizationService>();
}

/// <summary>bool to <see cref="Visibility"/>, optionally inverted.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value switch
        {
            bool b => b,
            null => false,
            int i => i != 0,
            _ => true,
        };

        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible ^ Invert;
}

/// <summary>Shows the element only when the bound value is not null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasValue = value is not null;
        if (Invert)
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Shows the element only when the bound string has content.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasText = value is string s && !string.IsNullOrWhiteSpace(s);
        if (Invert)
        {
            hasText = !hasText;
        }

        return hasText ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>0..100 interestingness to a theme-aware accent brush.</summary>
public sealed class ScoreToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Low = new(ColorHelper.FromArgb(255, 0x8A, 0x8A, 0x8E));
    private static readonly SolidColorBrush Medium = new(ColorHelper.FromArgb(255, 0xC0, 0x8A, 0x2E));
    private static readonly SolidColorBrush Good = new(ColorHelper.FromArgb(255, 0x2D, 0x7D, 0x9A));
    private static readonly SolidColorBrush High = new(ColorHelper.FromArgb(255, 0x6C, 0x3F, 0xC4));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var score = System.Convert.ToDouble(value ?? 0d, System.Globalization.CultureInfo.InvariantCulture);

        if (score >= 85)
        {
            return Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) && accent is Brush accentBrush
                ? accentBrush
                : High;
        }

        return score switch
        {
            >= 70 => Good,
            >= 55 => Medium,
            _ => Low,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>0..100 to the 0..1 range a ProgressBar expects.</summary>
public sealed class ScoreToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var score = System.Convert.ToDouble(value ?? 0d, System.Globalization.CultureInfo.InvariantCulture);
        return Math.Clamp(score, 0, 100);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is long bytes ? FormatHelpers.FileSize(bytes) : "—";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class DateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            DateTimeOffset dto => FormatHelpers.Date(dto),
            DateTime dt => FormatHelpers.Date(new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))),
            _ => "—",
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Turns a category key into its localized label.</summary>
public sealed class CategoryLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "unknown";
        }

        return ConverterServices.Localization?.Get($"Category_{key}") ?? key;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Picks a Segoe Fluent Icons glyph for a file family.</summary>
public sealed class FileGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var extension = value as string;
        return FileTypeCatalog.FamilyOf(extension) switch
        {
            FileFamily.Code or FileFamily.Project => "\uE943",
            FileFamily.Image => "\uE8B9",
            FileFamily.Audio => "\uE8D6",
            FileFamily.Video => "\uE714",
            FileFamily.Archive => "\uE7B8",
            FileFamily.GameSave => "\uE7FC",
            FileFamily.Configuration => "\uE713",
            FileFamily.Log => "\uE7C3",
            FileFamily.Executable => "\uE756",
            FileFamily.Design or FileFamily.ThreeD => "\uE790",
            FileFamily.Font => "\uE8D2",
            _ => "\uE8A5",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a local file path into a downsampled bitmap. Decoding at a fixed width keeps a 40 megapixel
/// photo from being loaded at full resolution just to render a preview.
/// </summary>
public sealed class PathToThumbnailConverter : IValueConverter
{
    public int DecodeWidth { get; set; } = 720;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
            {
                DecodePixelWidth = DecodeWidth,
                UriSource = new Uri(path, UriKind.Absolute),
            };

            return bitmap;
        }
        catch (Exception)
        {
            // Unsupported or unreadable image: the metadata-only preview stays visible.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Formats a 0..100 value as an integer percentage label.</summary>
public sealed class PercentToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var number = System.Convert.ToDouble(value ?? 0d, System.Globalization.CultureInfo.InvariantCulture);
        return $"{number:0}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
