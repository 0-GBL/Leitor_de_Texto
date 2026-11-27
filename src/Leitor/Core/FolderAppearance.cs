using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Leitor.Core;

public sealed record FolderAppearance(string? Icon = null, string? Color = null)
{
    public const string Inherit = "inherit";
    public static FolderAppearance Suggested { get; } = new();
}

public sealed record FolderVisual(string Icon, string Color, int InheritedLevels = 0);

public static class FolderAppearanceCatalog
{
    public static IReadOnlyList<(string Id, string Name)> Icons { get; } =
    [
        ("LibraryFolder", "Pasta"), ("Education", "Educação"), ("Calendar", "Calendário"),
        ("Book", "Livros"), ("Programming", "Programação"), ("Database", "Banco de dados"),
        ("Layers", "Organização"), ("Document", "Documento")
    ];

    public static IReadOnlyList<(string Id, string Name)> Colors { get; } =
    [
        ("green", "Verde"), ("blue", "Azul"), ("purple", "Roxo"),
        ("yellow", "Amarelo"), ("orange", "Laranja"), ("red", "Vermelho suave")
    ];

    private static readonly (string Icon, string Color, string Pattern)[] Suggestions =
    [
        ("Database", "purple", @"\b(big\s+data|banco\s+de\s+dados|dados|sql|hadoop)\b"),
        ("Programming", "blue", @"\b(programacao|codigo|desenvolvimento|software|full\s+stack)\b"),
        ("Calendar", "blue", @"\b(periodo|semestre|calendario|agenda|cronograma)\b"),
        ("Education", "purple", @"\b(faculdade|universidade|graduacao|escola|curso|estudos)\b"),
        ("Book", "green", @"\b(livros?|leitura|bibliografia|literatura)\b"),
        ("Layers", "orange", @"\b(organizacao|projetos?|planejamento|referencias)\b"),
        ("Document", "red", @"\b(documentos?|relatorios?|anotacoes|resumos?|apostilas)\b")
    ];

    public static FolderVisual Suggest(string name)
    {
        string normalized = string.Concat(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
        foreach (var (icon, color, pattern) in Suggestions)
            if (Regex.IsMatch(normalized, pattern, RegexOptions.CultureInvariant))
                return new(icon, color);
        return new("LibraryFolder", "yellow");
    }

    public static FolderVisual Resolve(string name, FolderAppearance appearance, FolderVisual? parent)
    {
        var suggested = Suggest(name);
        string icon = appearance.Icon ?? suggested.Icon;
        if (appearance.Color == FolderAppearance.Inherit && parent != null)
            return new(icon, parent.Color, Math.Min(parent.InheritedLevels + 1, 3));
        return new(icon, appearance.Color is null or FolderAppearance.Inherit ? suggested.Color : appearance.Color);
    }

    internal static string? LegacyColor(string color) => color.ToUpperInvariant() switch
    {
        "#3D865F" => "green",
        "#477FB7" => "blue",
        "#9272AA" => "purple",
        "#A77220" => "yellow",
        _ => null
    };
}
