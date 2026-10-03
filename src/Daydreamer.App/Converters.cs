using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Daydreamer.App.Editor;
using Daydreamer.App.Theming;

namespace Daydreamer.App;

/// <summary>"#1971C2" → a brush (transparent if empty or invalid).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        WpfStyles.TryParseColor(value as string) is null ? Brushes.Transparent : WpfStyles.Brush(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A chosen color → a brush that's readable on the current light/dark page.</summary>
public sealed class ReadableBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string color && WpfStyles.TryParseColor(color) != null
            ? WpfStyles.ReadableBrush(color, ThemeManager.Palette.Paper)
            : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A font name → FontFamily, falling back to the UI font when the name is empty.</summary>
public sealed class FontNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } name ? new FontFamily(name) : SystemFonts.MessageFontFamily;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hides an element when the bound value is null (e.g. no character selected).</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value is null || (value is int count && count == 0);
        bool invert = parameter as string == "invert";
        return isNull ^ invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → false. Used so clicking the color button while its pop-up is open just closes it.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>An alignment → its toolbar icon (the IconAlign… geometries in Themes/Daydreamer.xaml).</summary>
public sealed class AlignmentIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.TryFindResource("IconAlign" + (value as Daydreamer.Core.Models.TextAlign? ?? Daydreamer.Core.Models.TextAlign.Left));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Alignment ⇄ the words in the side panel's alignment box ("Default" means inherit).</summary>
public sealed class AlignmentChoiceConverter : IValueConverter
{
    public static IReadOnlyList<string> Choices { get; } = ["Default", "Left", "Center", "Right", "Justify"];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Daydreamer.Core.Models.TextAlign alignment ? alignment.ToString() : "Default";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Enum.TryParse<Daydreamer.Core.Models.TextAlign>(value as string, out var alignment) ? alignment : null;
}

/// <summary>An element type → the name people see ("Description", "SFX", "List item").</summary>
public sealed class ElementTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Daydreamer.Core.Models.ElementType type ? Daydreamer.Core.Models.StyleSheet.DisplayNameOf(type) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A count → "12 lines" / "1 line".</summary>
public sealed class LineCountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n ? (n == 1 ? "1 line" : $"{n} lines") : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A date → "Edited 3:42 PM" today, otherwise "Edited Oct 2".</summary>
public sealed class EditedDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime date || date == DateTime.MinValue) return "";
        return date.Date == DateTime.Today ? $"Edited {date:t}" : $"Edited {date:MMM d}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
