using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Leitor.Services;

public sealed class Vault : IDisposable
{
    private readonly string path, attemptPath;
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private byte[]? key;
    private sealed record Envelope(string Salt, string Wrapped);
    private sealed record Attempts(string Salt, int Failures, DateTimeOffset NotBefore);
    public bool Configured => File.Exists(path);
    public bool Unlocked { get { lock (gate) return key != null; } }
    public Vault(string directory, TimeProvider? clock = null)
    {
        AtomicFiles.EnsurePrivateDirectory(directory); path = Path.Combine(directory, "vault.json");
        attemptPath = Path.Combine(directory, "auth-state.bin"); this.clock = clock ?? TimeProvider.System;
    }
    public static void ValidateNewPin(string pin)
    { if (pin.Length is < 6 or > 64 || !pin.All(char.IsAsciiDigit)) throw new InvalidOperationException("Use um PIN de 6 a 64 dígitos."); }
    public void Create(string pin)
    {
        lock (gate)
        {
            if (Configured) throw new InvalidOperationException("Já existe um PIN. Use Alterar PIN para modificá-lo.");
            ValidateNewPin(pin); byte[] master = RandomNumberGenerator.GetBytes(32);
            try { WriteEnvelope(pin, master); key = master; }
            catch { CryptographicOperations.ZeroMemory(master); throw; }
        }
    }
    private Envelope ReadEnvelope() => JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(path)) ?? throw new CryptographicException("Material de proteção inválido.");
    public TimeSpan RemainingLockout
    {
        get { lock (gate) { if (!Configured) return TimeSpan.Zero; var remaining = ReadAttempts(ReadEnvelope().Salt).NotBefore - clock.GetUtcNow(); return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero; } }
    }
    public bool Unlock(string pin)
    {
        lock (gate)
        {
            var envelope = ReadEnvelope(); var attempts = ReadAttempts(envelope.Salt);
            if (attempts.NotBefore > clock.GetUtcNow()) return false;
            byte[] derived = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(envelope.Salt), 600_000, HashAlgorithmName.SHA256, 32);
            byte[]? wrapped = null, candidate = null;
            try
            {
                wrapped = ProtectedData.Unprotect(Convert.FromBase64String(envelope.Wrapped), null, DataProtectionScope.CurrentUser);
                candidate = Decrypt(wrapped, derived);
                WriteAttempts(new(envelope.Salt, 0, DateTimeOffset.MinValue));
                Lock(); key = candidate; candidate = null; return true;
            }
            catch (CryptographicException)
            {
                int failures = Math.Min(20, attempts.Failures + 1);
                WriteAttempts(new(envelope.Salt, failures, clock.GetUtcNow().AddSeconds(Math.Min(30, Math.Pow(2, failures)))));
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
                if (wrapped != null) CryptographicOperations.ZeroMemory(wrapped);
                if (candidate != null) CryptographicOperations.ZeroMemory(candidate);
            }
        }
    }
    private Attempts ReadAttempts(string salt)
    {
        if (!File.Exists(attemptPath)) return new(salt, 0, DateTimeOffset.MinValue);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(attemptPath), null, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<Attempts>(plain) ?? throw new CryptographicException();
            if (state.Salt != salt) return new(salt, 0, DateTimeOffset.MinValue);
            return state;
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        {
            // Corruption delays authentication once; it never destroys the encryption key or documents.
            var reset = new Attempts(salt, 1, clock.GetUtcNow().AddSeconds(30)); WriteAttempts(reset); return reset;
        }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }
    private void WriteAttempts(Attempts state)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        try { AtomicFiles.Write(attemptPath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void ChangePin(string pin)
    {
        lock (gate) { ValidateNewPin(pin); WriteEnvelope(pin, key ?? throw new InvalidOperationException("Desbloqueie antes de alterar o PIN.")); }
    }
    private void WriteEnvelope(string pin, byte[] master)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);
        byte[]? wrapped = null;
        try
        {
            wrapped = Encrypt(master, derived);
            var envelope = new Envelope(Convert.ToBase64String(salt), Convert.ToBase64String(ProtectedData.Protect(wrapped, null, DataProtectionScope.CurrentUser)));
            AtomicFiles.Write(path, JsonSerializer.SerializeToUtf8Bytes(envelope));
            // Salt changes invalidate the old attempt state without creating a multi-file commit dependency.
        }
        finally { CryptographicOperations.ZeroMemory(derived); if (wrapped != null) CryptographicOperations.ZeroMemory(wrapped); }
    }
    public byte[] Seal(byte[] value, string? documentId = null)
    {
        lock (gate)
        {
            var secret = key ?? throw new InvalidOperationException("Desbloqueie com o PIN.");
            if (documentId == null) return Encrypt(value, secret);
            return "LVR2"u8.ToArray().Concat(Encrypt(value, secret, Encoding.UTF8.GetBytes("document:" + documentId))).ToArray();
        }
    }
    public byte[] Open(byte[] value, string? documentId = null)
    {
        lock (gate)
        {
            var secret = key ?? throw new InvalidOperationException("Desbloqueie com o PIN.");
            if (value.AsSpan().StartsWith("LVR2"u8))
            {
                if (documentId == null) throw new CryptographicException("Documento sem identidade.");
                return Decrypt(value[4..], secret, Encoding.UTF8.GetBytes("document:" + documentId));
            }
            return Decrypt(value, secret); // 1.0 / 1.1 payload compatibility.
        }
    }
    public void Lock() { lock (gate) { if (key != null) CryptographicOperations.ZeroMemory(key); key = null; } }
    public void Remove() { lock (gate) { File.Delete(path); if (File.Exists(attemptPath)) File.Delete(attemptPath); Lock(); } }
    public void Dispose() => Lock();
    private static byte[] Encrypt(byte[] data, byte[] secret, byte[]? aad = null)
    {
        byte[] output = new byte[28 + data.Length]; RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(secret, 16); aes.Encrypt(output.AsSpan(0, 12), data, output.AsSpan(28), output.AsSpan(12, 16), aad); return output;
    }
    private static byte[] Decrypt(byte[] data, byte[] secret, byte[]? aad = null)
    {
        if (data.Length < 28) throw new CryptographicException("Dados protegidos inválidos.");
        byte[] output = new byte[data.Length - 28];
        using var aes = new AesGcm(secret, 16);
        try { aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), output, aad); return output; }
        catch { CryptographicOperations.ZeroMemory(output); throw; }
    }
}
