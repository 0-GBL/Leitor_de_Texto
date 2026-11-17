using System.Windows;

namespace Leitor.Services;
public static class ThemeService
{
    private static bool? applied;
    public static void Apply(bool dark)
    {
        if (applied == dark) return;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Leitor;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute) };
        if (dictionaries.Count == 0) dictionaries.Add(next); else dictionaries[0] = next;
        applied = dark;
    }
}
