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
public sealed class Vault : IDisposable
{
    private readonly string path;
    private byte[]? key;
    public bool Configured => File.Exists(path);
    public bool Unlocked => key != null;
    private sealed record Envelope(string Salt, string Wrapped);
    public Vault(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "vault.json"); }
    public void Create(string pin)
    {
        if (Configured) throw new InvalidOperationException("Já existe um PIN. Desative-o antes de cadastrar outro.");
        if (pin.Length < 6 || !pin.All(char.IsAsciiDigit)) throw new InvalidOperationException("Use um PIN com pelo menos 6 dígitos.");
        byte[] salt = RandomNumberGenerator.GetBytes(32), master = RandomNumberGenerator.GetBytes(32);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);
        try
        {
            byte[] wrapped = Encrypt(master, derived);
            byte[] protectedBytes = ProtectedData.Protect(wrapped, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Envelope(Convert.ToBase64String(salt), Convert.ToBase64String(protectedBytes))));
            File.Move(path + ".tmp", path, true); key = master;
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public bool Unlock(string pin)
    {
        var e = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path))!;
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(e.Salt), 600_000, HashAlgorithmName.SHA256, 32);
        try
        {
            var wrapped = ProtectedData.Unprotect(Convert.FromBase64String(e.Wrapped), null, DataProtectionScope.CurrentUser);
            var next = Decrypt(wrapped, derived); Lock(); key = next; return true;
        }
        catch (CryptographicException) { return false; }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
    public byte[] Seal(byte[] value) => Encrypt(value, key ?? throw new InvalidOperationException("Desbloqueie com o PIN."));
    public byte[] Open(byte[] value) => Decrypt(value, key ?? throw new InvalidOperationException("Desbloqueie com o PIN."));
    public void Lock() { if (key != null) CryptographicOperations.ZeroMemory(key); key = null; }
    public void Remove() { Lock(); File.Delete(path); }
    public void Dispose() => Lock();
    private static byte[] Encrypt(byte[] data, byte[] secret)
    {
        byte[] output = new byte[28 + data.Length]; RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(secret, 16); aes.Encrypt(output.AsSpan(0, 12), data, output.AsSpan(28), output.AsSpan(12, 16)); return output;
    }
    private static byte[] Decrypt(byte[] data, byte[] secret)
    {
        if (data.Length < 28) throw new CryptographicException("Dados protegidos inválidos.");
        byte[] output = new byte[data.Length - 28];
        using var aes = new AesGcm(secret, 16); aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), output); return output;
    }
}
public sealed class LibraryStore
{
    private readonly string connectionString;
    private readonly Vault vault;
    public LibraryStore(string directory, Vault vault)
    {
        Directory.CreateDirectory(directory); this.vault = vault;
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db"), Pooling = false }.ToString();
        using var db = Connect(); using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS documents(id TEXT PRIMARY KEY, protected INTEGER NOT NULL, payload BLOB NOT NULL); PRAGMA user_version=1;"; cmd.ExecuteNonQuery();
    }
    private SqliteConnection Connect() { var db = new SqliteConnection(connectionString); db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA secure_delete=ON; PRAGMA busy_timeout=5000;"; c.ExecuteNonQuery(); return db; }
    public void Save(Document document)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(document);
        if (document.Protected) data = vault.Seal(data);
        using var db = Connect(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO documents VALUES($id,$p,$v) ON CONFLICT(id) DO UPDATE SET protected=$p,payload=$v";
        cmd.Parameters.AddWithValue("$id", document.Id); cmd.Parameters.AddWithValue("$p", document.Protected ? 1 : 0); cmd.Parameters.AddWithValue("$v", data); cmd.ExecuteNonQuery();
    }
    public List<Document> Load()
    {
        var result = new List<Document>(); using var db = Connect(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,protected,payload FROM documents";
        using var rows = cmd.ExecuteReader();
        while (rows.Read())
        {
            bool secure = rows.GetBoolean(1);
            if (secure && !vault.Unlocked) { result.Add(new() { Id = rows.GetString(0), Title = "Texto protegido", Protected = true }); continue; }
            byte[] bytes = (byte[])rows[2]; if (secure) bytes = vault.Open(bytes);
            result.Add(JsonSerializer.Deserialize<Document>(bytes)!);
        }
        return result.OrderByDescending(x => x.Saved).ToList();
    }
    public void Delete(string id) { using var db = Connect(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM documents WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
    public void DisableProtection()
    {
        if (!vault.Unlocked) throw new InvalidOperationException("Desbloqueie antes de desativar a proteção.");
        var docs = Load(); using var db = Connect(); using var tx = db.BeginTransaction();
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
    public SettingsStore(string root) { Directory.CreateDirectory(root); path = Path.Combine(root, "settings.json"); }
    public Settings Load() => File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new() : new();
    public void Save(Settings settings) { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); }
}
