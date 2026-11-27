using System.IO;
using System.Text.Json;
using Leitor.Core;
using Microsoft.Data.Sqlite;

namespace Leitor.Services;

public sealed partial class LibraryStore
{
    private static void MigrateFolderAppearance(SqliteConnection db, SqliteTransaction tx, string directory)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS folder_appearance (
                folder TEXT PRIMARY KEY REFERENCES folders(id) ON DELETE CASCADE,
                icon TEXT,
                color TEXT
            );
            """;
        command.ExecuteNonQuery();

        string settingsPath = Path.Combine(directory, "settings.json");
        if (!File.Exists(settingsPath)) return;
        Settings? settings;
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)); }
        catch (JsonException) { return; }
        if (settings?.FolderColors is not { Count: > 0 } colors) return;

        command.CommandText = """
            INSERT OR IGNORE INTO folder_appearance(folder,color)
            SELECT id,$color FROM folders WHERE id=$id
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var color = command.Parameters.Add("$color", SqliteType.Text);
        foreach (var entry in colors)
        {
            if (entry.Value == null || FolderAppearanceCatalog.LegacyColor(entry.Value) is not string family) continue;
            id.Value = entry.Key;
            color.Value = family;
            command.ExecuteNonQuery();
        }
    }

    public void SaveFolderAppearance(string id, FolderAppearance appearance)
    {
        if (appearance.Icon != null && !FolderAppearanceCatalog.Icons.Any(icon => icon.Id == appearance.Icon))
            throw new ArgumentException("Ícone desconhecido.", nameof(appearance));
        if (appearance.Color != null && appearance.Color != FolderAppearance.Inherit &&
            !FolderAppearanceCatalog.Colors.Any(color => color.Id == appearance.Color))
            throw new ArgumentException("Cor desconhecida.", nameof(appearance));

        using var db = Connect();
        WriteFolderAppearance(db, null, id, appearance);
    }

    private static void WriteFolderAppearance(SqliteConnection db, SqliteTransaction? tx, string id, FolderAppearance appearance)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO folder_appearance(folder,icon,color) VALUES ($id,$icon,$color)
            ON CONFLICT(folder) DO UPDATE SET icon=excluded.icon,color=excluded.color
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$icon", (object?)appearance.Icon ?? DBNull.Value);
        command.Parameters.AddWithValue("$color", (object?)appearance.Color ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
