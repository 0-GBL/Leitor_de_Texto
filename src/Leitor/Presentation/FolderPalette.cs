using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Leitor.Core;

namespace Leitor.Presentation;

public sealed class FolderPalette : IMultiValueConverter
{
    private static readonly IReadOnlyDictionary<string, (Color Light, Color Dark)> BaseColors =
        new Dictionary<string, (Color, Color)>
        {
            ["green"] = (Color.FromRgb(31, 101, 67), Color.FromRgb(109, 201, 154)),
            ["blue"] = (Color.FromRgb(35, 85, 154), Color.FromRgb(110, 179, 245)),
            ["purple"] = (Color.FromRgb(101, 58, 150), Color.FromRgb(175, 150, 247)),
            ["yellow"] = (Color.FromRgb(143, 93, 15), Color.FromRgb(235, 183, 89)),
            ["orange"] = (Color.FromRgb(151, 65, 21), Color.FromRgb(239, 154, 101)),
            ["red"] = (Color.FromRgb(143, 55, 62), Color.FromRgb(231, 141, 150))
        };

    internal static Brush Tint(FolderVisual visual, bool dark, bool badge = false)
    {
        var colors = BaseColors.GetValueOrDefault(visual.Color, BaseColors["yellow"]);
        Color source = dark ? colors.Dark : colors.Light;
        double blend = visual.InheritedLevels * (dark ? .11 : .065);
        byte Lighten(byte value) => (byte)Math.Round(value + (255 - value) * blend);
        var brush = new SolidColorBrush(Color.FromArgb(badge ? (byte)(dark ? 42 : 28) : (byte)255,
            Lighten(source.R), Lighten(source.G), Lighten(source.B)));
        brush.Freeze();
        return brush;
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [FolderVisual visual, bool dark]
            ? Tint(visual, dark, Equals(parameter, "badge"))
            : DependencyProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
