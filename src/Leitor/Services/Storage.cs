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
public sealed partial class LibraryStore
{
    private readonly string connectionString;
    private readonly Vault vault;
    public LibraryStore(string directory, Vault vault)
    {
        AtomicFiles.EnsurePrivateDirectory(directory); this.vault = vault;
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Pooling = false }.ToString();
        Migrate(directory);
    }
    private SqliteConnection Connect() { var db = new SqliteConnection(connectionString); db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON; PRAGMA busy_timeout=5000;"; c.ExecuteNonQuery(); return db; }
    public void Save(Document document)
    {
        using var db = Connect(); using var tx = db.BeginTransaction();
        var previous = ReadDocument(db, tx, document.Id, assignmentOnly: true);
        if (previous != null)
        {
            RequireReadable(previous);
            document.FolderId = previous.FolderId;
            document.Created ??= previous.Created ?? previous.Saved;
        }
        WriteDocument(db, tx, document);
        tx.Commit();
    }
    private void WriteDocument(SqliteConnection db, SqliteTransaction tx, Document document)
    {
        RequireReadable(document);
        document.Created ??= document.Saved;
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(document);
        if (document.Protected) { var plain = data; try { data = vault.Seal(plain, document.Id); } finally { CryptographicOperations.ZeroMemory(plain); } }
        using var cmd = db.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO documents(id,protected,payload) VALUES($id,$p,$v) ON CONFLICT(id) DO UPDATE SET protected=$p,payload=$v";
        cmd.Parameters.AddWithValue("$id",document.Id); cmd.Parameters.AddWithValue("$p",document.Protected?1:0); cmd.Parameters.AddWithValue("$v",data); cmd.ExecuteNonQuery();
        Catalog(db,tx,document);
    }
    public List<Document> Load()
    {
        var result = new List<Document>(); using var db = Connect(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT d.id,d.protected,CASE WHEN d.protected=0 OR $unlocked THEN d.payload END,c.folder FROM documents d LEFT JOIN document_catalog c USING(id)";
        cmd.Parameters.AddWithValue("$unlocked",vault.Unlocked);
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            bool secure = rows.GetBoolean(1);
            if (secure && !vault.Unlocked) { result.Add(new() { Id = rows.GetString(0), Title = "Texto protegido", Protected = true, IsLocked = true }); continue; }
            var doc = rows.IsDBNull(2) ? new Document { Id = rows.GetString(0), Title = "Texto protegido", Protected = true, IsLocked = true }
                : Decode(rows.GetString(0), secure, (byte[])rows[2]);
            if (!secure) doc.FolderId = rows.IsDBNull(3) ? null : rows.GetString(3);
            result.Add(doc);
        }
        return result.OrderByDescending(x => x.Saved).ToList();
    }
    public void Delete(string id) { using var db = Connect(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM documents WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
    public void DisableProtection()
    {
        if(!vault.Unlocked)throw new InvalidOperationException("Desbloqueie antes de desativar a proteção.");
        using var db=Connect();using var tx=db.BeginTransaction();var ids=new List<(string Id,bool Protected)>();
        using(var c=db.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT id,protected FROM documents";using var r=c.ExecuteReader();while(r.Read())ids.Add((r.GetString(0),r.GetBoolean(1)));}
        foreach(var entry in ids)
        {
            Document doc;using(var c=db.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT payload FROM documents WHERE id=$id";c.Parameters.AddWithValue("$id",entry.Id);doc=Decode(entry.Id,entry.Protected,(byte[])c.ExecuteScalar()!);}
            if(doc.IsUnavailable||doc.IsLocked)throw new InvalidOperationException("Há documentos com integridade inválida. A proteção foi mantida para evitar perda de dados.");
            if(!entry.Protected)continue;
            doc.Protected=false;doc.Created??=doc.Saved;using var update=db.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE documents SET protected=0,payload=$v WHERE id=$id";update.Parameters.AddWithValue("$id",doc.Id);update.Parameters.AddWithValue("$v",JsonSerializer.SerializeToUtf8Bytes(doc));update.ExecuteNonQuery();Catalog(db,tx,doc);
        }
        tx.Commit();vault.Remove();
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
