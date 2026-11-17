using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Leitor.Core;
using Microsoft.Data.Sqlite;

namespace Leitor.Services;

public static class AppPaths
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("LEITOR_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Leitor");
    public static string Runtime => Path.Combine(Root, "piper");
    public static string Voices => Path.Combine(Root, "voices");
}
public sealed class LibraryStore
{
    private readonly string connectionString;
    private readonly Vault vault;
    public LibraryStore(string directory, Vault vault)
    {
        AtomicFiles.EnsurePrivateDirectory(directory); this.vault = vault;
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Pooling = false }.ToString();
        using var db = Connect(); using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS documents(id TEXT PRIMARY KEY, protected INTEGER NOT NULL, payload BLOB NOT NULL); PRAGMA user_version=1;"; cmd.ExecuteNonQuery();
    }
    private SqliteConnection Connect() { var db = new SqliteConnection(connectionString); db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA secure_delete=ON; PRAGMA busy_timeout=5000;"; c.ExecuteNonQuery(); return db; }
    public void Save(Document document)
    {
        if (document.IsUnavailable) throw new InvalidOperationException("Documento indisponível não pode ser sobrescrito.");
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(document);
        if (document.Protected) { var plain = data; try { data = vault.Seal(plain, document.Id); } finally { CryptographicOperations.ZeroMemory(plain); } }
        using var db = Connect(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO documents VALUES($id,$p,$v) ON CONFLICT(id) DO UPDATE SET protected=$p,payload=$v";
        cmd.Parameters.AddWithValue("$id", document.Id); cmd.Parameters.AddWithValue("$p", document.Protected ? 1 : 0); cmd.Parameters.AddWithValue("$v", data);
        using var tx = db.BeginTransaction(); cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
    }
    public List<Document> Load()
    {
        var result = new List<Document>(); using var db = Connect(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,protected,payload FROM documents";
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            bool secure = rows.GetBoolean(1);
            if (secure && !vault.Unlocked) { result.Add(new() { Id = rows.GetString(0), Title = "Texto protegido", Protected = true, IsLocked = true }); continue; }
            byte[] bytes = (byte[])rows[2]; byte[]? plain = null;
            try
            {
                if (!secure && bytes.AsSpan().StartsWith("LVR2"u8)) throw new CryptographicException("Metadados alterados.");
                plain = secure ? vault.Open(bytes, rows.GetString(0)) : bytes;
                var doc = JsonSerializer.Deserialize<Document>(plain) ?? throw new JsonException();
                if (doc.Id != rows.GetString(0) || doc.Protected != secure) throw new CryptographicException("Identidade inválida.");
                result.Add(doc);
            }
            catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
            { result.Add(new() { Id = rows.GetString(0), Title = "Documento indisponível", Protected = secure, IsUnavailable = true }); }
            finally { if (secure && plain != null) CryptographicOperations.ZeroMemory(plain); }
        }
        return result.OrderByDescending(x => x.Saved).ToList();
    }
    public void Delete(string id) { using var db = Connect(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM documents WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
    public void DisableProtection()
    {
        if (!vault.Unlocked) throw new InvalidOperationException("Desbloqueie antes de desativar a proteção.");
        var docs = Load(); using var db = Connect(); using var tx = db.BeginTransaction();
        if (docs.Any(x => x.IsUnavailable)) throw new InvalidOperationException("Há documentos com integridade inválida. A proteção foi mantida para evitar perda de dados.");
        foreach (var doc in docs.Where(x => x.Protected))
        {
            doc.Protected = false; using var cmd = db.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE documents SET protected=0,payload=$v WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", doc.Id); cmd.Parameters.AddWithValue("$v", JsonSerializer.SerializeToUtf8Bytes(doc)); cmd.ExecuteNonQuery();
        }
        tx.Commit(); vault.Remove();
    }
}
public sealed class SettingsStore
{
    private readonly string path;
    public SettingsStore(string root) { AtomicFiles.EnsurePrivateDirectory(root); path = Path.Combine(root, "settings.json"); }
    public Settings Load()
    {
        if (!File.Exists(path)) return new();
        try { var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new(); settings.Normalize(); return settings; }
        catch (JsonException) { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false); return new(); }
    }
    public void Save(Settings settings) { settings.Normalize(); AtomicFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true })); }
}
