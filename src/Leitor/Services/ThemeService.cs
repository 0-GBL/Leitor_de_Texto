using System.Windows;

namespace Leitor.Services;
public static class ThemeService
{
    private static bool? applied;
    private static readonly Dictionary<bool, ResourceDictionary> cache = [];
    public static void Apply(bool dark)
    {
        if (applied == dark) return;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (!cache.TryGetValue(dark, out var next)) cache[dark] = next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Leitor;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute) };
        if (dictionaries.Count == 0) dictionaries.Add(next); else dictionaries[0] = next;
        applied = dark;
    }
}
