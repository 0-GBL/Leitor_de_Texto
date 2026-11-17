using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Leitor.Services;
public sealed record VoiceModel(string Id, string Name, string Path)
{
    public string ModelSha256 => Id switch {
        "pt_BR-faber-medium" => "75e913eb463db0b6ac7ddf68d3e4c9ac19ace36dc3b710a81039b448352b8ffc",
        "pt_BR-edresson-low" => "de4cecee38b30bb1a6378a337af605d59f0c377df702c6a6752870db8991cd84",
        "en_US-ljspeech-medium" => "6f52a751e2349abe7a76735eb09dc1875298c77ea2342ffd2fef79ff81b87f22", _ => throw new InvalidOperationException("Modelo desconhecido.") };
    public string ConfigSha256 => Id switch {
        "pt_BR-faber-medium" => "6b6801319e5a70aa4d75c63343515f55d1cd671ecd1ac511586bc16216812261",
        "pt_BR-edresson-low" => "f138992d2e777d1e3aa0bbb14c2d324307b0f342c1bcf20978765b3bea506c56",
        "en_US-ljspeech-medium" => "141d612cc0a95ed7efc1ca936b845c2364967f2e9217c5dbfcf69fc4d6c65860", _ => throw new InvalidOperationException("Modelo desconhecido.") };
    public override string ToString() => Name;
    public string BaseUrl => "https://huggingface.co/rhasspy/piper-voices/resolve/main/" + Path + "/";
}
public sealed class VoiceDownloads
{
    public static IReadOnlyList<VoiceModel> Catalog { get; } = [
        new("pt_BR-faber-medium", "Português brasileiro · Faber · média", "pt/pt_BR/faber/medium"),
        new("pt_BR-edresson-low", "Português brasileiro · Edresson · leve", "pt/pt_BR/edresson/low"),
        new("en_US-ljspeech-medium", "Inglês americano · LJ Speech · média (feminina)", "en/en_US/ljspeech/medium")];
    private static readonly HttpClient shared = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly HttpClient http;
    public VoiceDownloads(HttpClient? client = null) => http = client ?? shared;
    public Task<string> LicenseAsync(VoiceModel model, CancellationToken token) => http.GetStringAsync(model.BaseUrl + "MODEL_CARD", token);
    // No editor/library content is accepted by this service.
    public async Task DownloadAsync(VoiceModel model, string license, string directory, IProgress<string>? progress, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { await DownloadOnceAsync(model, license, directory, progress, token); return; }
            catch (Exception error) when (!token.IsCancellationRequested && attempt < 2 && error is HttpRequestException or IOException or TimeoutException)
            { progress?.Report($"Conexão interrompida. Nova tentativa ({attempt + 2}/3)…"); }
        }
    }
    private async Task DownloadOnceAsync(VoiceModel model, string license, string directory, IProgress<string>? progress, CancellationToken token)
    {
        if (!Catalog.Contains(model)) throw new ArgumentException("Modelo fora do catálogo.");
        if (string.IsNullOrWhiteSpace(license)) throw new ArgumentException("Leia a ficha de licença antes do download.");
        Directory.CreateDirectory(directory); string target = System.IO.Path.Combine(directory, model.Id + ".onnx");
        try
        {
            foreach (string extension in new[] { ".onnx", ".onnx.json" })
            {
                string file = System.IO.Path.Combine(directory, model.Id + extension);
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await http.GetAsync(model.BaseUrl + model.Id + extension, HttpCompletionOption.ResponseHeadersRead, idle.Token);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(idle.Token);
                await using var dest = File.Create(file + ".part");
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, idle.Token)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), token); total += read;
                    if (total > (extension == ".onnx" ? 256L * 1024 * 1024 : 2L * 1024 * 1024)) throw new InvalidDataException("Download excedeu o tamanho permitido para este recurso.");
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    progress?.Report($"Baixando {model.Id}{extension}: {total / 1048576.0:0.0} MB");
                }
                if (response.Content.Headers.ContentLength is long expected && total != expected) throw new IOException("Download incompleto.");
            }
            using (var config = JsonDocument.Parse(await File.ReadAllTextAsync(target + ".json.part", token)))
                if (config.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32() <= 0) throw new IOException("Configuração de voz inválida.");
            if (new FileInfo(target + ".part").Length < 1024) throw new IOException("Modelo de voz inválido.");
            foreach (var item in new[] { (File: target + ".part", Hash: model.ModelSha256), (File: target + ".json.part", Hash: model.ConfigSha256) })
            {
                await using var downloaded = File.OpenRead(item.File);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, token));
                if (!hash.Equals(item.Hash, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("O recurso não corresponde ao hash confiável do catálogo. A instalação anterior foi preservada.");
            }
            token.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(target + ".MODEL_CARD", license, token);
            File.Move(target + ".json.part", target + ".json", true); File.Move(target + ".part", target, true);
            await using var data = File.OpenRead(target);
            await File.WriteAllTextAsync(target + ".sha256", Convert.ToHexString(await SHA256.HashDataAsync(data, token)), token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("O servidor de vozes ficou 30 segundos sem responder. Verifique a conexão e tente novamente."); }
        finally
        {
            foreach (string suffix in new[] { ".part", ".json.part" }) if (File.Exists(target + suffix)) File.Delete(target + suffix);
        }
    }
}
