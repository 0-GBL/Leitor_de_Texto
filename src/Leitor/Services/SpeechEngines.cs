using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Security;
using System.Collections.Concurrent;
using Leitor.Core;

namespace Leitor.Services;

internal sealed class PiperFailure(string operation, string message, bool model = false, bool initialization = false,
    string? workerType = null, string? workerMessage = null, string[]? workerFrames = null, Exception? cause = null)
    : InvalidOperationException(message, cause)
{
    public string Operation { get; } = operation;
    public string? WorkerType { get; } = workerType;
    public string? WorkerMessage { get; } = workerMessage;
    public string[]? WorkerFrames { get; } = workerFrames;
    public bool Model { get; } = model;
    public bool Initialization { get; } = initialization;
}

public interface ISpeechEngine
{
    Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token);
}
public sealed class WindowsSpeechEngine : ISpeechEngine
{
    public static List<Voice> Voices()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices().Where(x => x.Enabled).Select(x => x.VoiceInfo)
            .OrderByDescending(x => x.Culture.Name == "pt-BR")
            .Select(x => new Voice(x.Name, $"{x.Name} ({(x.Gender == VoiceGender.Female ? "feminina" : x.Gender == VoiceGender.Male ? "masculina" : "neutra")})", "Windows", x.Culture.Name)).ToList();
    }
    public Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var synth = new SpeechSynthesizer();
        if (options.Voice?.Engine == "Windows") synth.SelectVoice(options.Voice.Id);
        else
        {
            var pt = synth.GetInstalledVoices().FirstOrDefault(x => x.Enabled && x.VoiceInfo.Culture.Name == "pt-BR");
            if (pt != null) synth.SelectVoice(pt.VoiceInfo.Name);
        }
        using var stream = new MemoryStream(); synth.SetOutputToWaveStream(stream);
        using var cancel = token.Register(() => { try { synth.SpeakAsyncCancelAll(); } catch (Exception) { } });
        string pitch = options.Pitch.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        string rate = (options.Speed * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        string language = synth.Voice.Culture.Name;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synth.SpeakCompleted += (_, e) => { if (e.Error != null) completion.TrySetException(e.Error); else completion.TrySetResult(); };
        synth.SpeakSsmlAsync($"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{language}'><prosody rate='{rate}' pitch='{pitch}'>{SecurityElement.Escape(text)}</prosody></speak>");
        completion.Task.GetAwaiter().GetResult(); token.ThrowIfCancellationRequested();
        synth.SetOutputToNull(); return stream.ToArray();
    }, token);
}
public sealed class PiperSpeechEngine : ISpeechEngine, IDisposable
{
    private Process? worker;
    private bool workerAnswered;
    private readonly SemaphoreSlim gate = new(1, 1);
    public static bool Installed => File.Exists(Path.Combine(AppPaths.Runtime, "python.exe")) && File.Exists(Path.Combine(AppPaths.Runtime, "ready"));
    public static List<Voice> Voices()
    {
        if (!Directory.Exists(AppPaths.Voices)) return [];
        return Directory.GetFiles(AppPaths.Voices, "*.onnx").Where(x => File.Exists(x + ".json"))
            .Select(x => new Voice(Path.GetFileNameWithoutExtension(x), Path.GetFileNameWithoutExtension(x), "Piper", Path.GetFileName(x).Split('-')[0].Replace('_', '-'), x)).ToList();
    }
    private void Start()
    {
        if (worker is { HasExited: false }) return;
        if (!Installed) throw new InvalidOperationException("Instale ou repare o Piper em Vozes offline, na lateral de narração.");
        worker?.Dispose();
        var info = new ProcessStartInfo(Path.Combine(AppPaths.Runtime, "python.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false) };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "piper_worker.py"));
        info.Environment["PYTHONNOUSERSITE"] = "1";
        info.Environment["HF_HUB_OFFLINE"] = "1";
        worker = Process.Start(info) ?? throw new PiperFailure("start-worker", "O processo Piper não foi criado.", initialization: true);
        workerAnswered = false;
        // Drain stderr to prevent native runtime diagnostics from blocking the pipe. No text logs are kept.
        worker.ErrorDataReceived += (_, _) => { }; worker.BeginErrorReadLine();
    }
    public async Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            try { Start(); }
            catch (System.ComponentModel.Win32Exception error)
            {
                throw new PiperFailure("start-worker", "O Windows não iniciou o processo Piper.", initialization: true, cause: error);
            }
            var current = worker!;
            bool starting = !workerAnswered;
            using var cancel = token.Register(() => { try { if (!current.HasExited) current.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
            string? line;
            string operation = "write-request";
            try
            {
                await current.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { text, model = options.Voice!.Model, speed = options.Speed }).AsMemory(), token);
                operation = "read-response";
                line = await current.StandardOutput.ReadLineAsync(token);
            }
            catch (IOException error)
            {
                token.ThrowIfCancellationRequested();
                throw new PiperFailure(operation, "O canal de comunicação com o Piper falhou.", initialization: starting, cause: error);
            }
            token.ThrowIfCancellationRequested();
            if (line == null)
                throw new PiperFailure(operation, "O canal de saída do Piper foi fechado antes da resposta.", initialization: starting);
            workerAnswered = true;
            using var json = JsonDocument.Parse(line);
            if (json.RootElement.TryGetProperty("error", out var failure))
            {
                var response = json.RootElement;
                string? Field(string name) => response.TryGetProperty(name, out var value) ? value.GetString() : null;
                string[]? frames = response.TryGetProperty("frames", out var trace)
                    ? trace.EnumerateArray().Select(frame => frame.GetString()!).ToArray() : null;
                bool modelError = failure.GetString() == "model";
                throw new PiperFailure(failure.GetString() ?? "worker", "O worker Piper retornou uma falha.",
                    model: modelError, workerType: Field("type"), workerMessage: Field("message"), workerFrames: frames);
            }
            return Convert.FromBase64String(json.RootElement.GetProperty("audio").GetString()!);
        }
        finally { gate.Release(); }
    }
    public void Dispose() { try { if (worker is { HasExited: false }) worker.Kill(true); } catch (InvalidOperationException) { } worker?.Dispose(); worker = null; }
}
public sealed class SpeechService : ISpeechEngine, IDisposable
{
    public void ReleasePiper() { piper.Dispose(); failedVoices.Clear(); }
    private readonly WindowsSpeechEngine windows = new();
    private readonly PiperSpeechEngine piper = new();
    private readonly ConcurrentDictionary<string, byte> failedVoices = new();
    public event Action<string>? Notice;
    public async Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token)
    {
        if (options.Voice?.Engine != "Piper") return await windows.GenerateAsync(text, options, token);
        if (failedVoices.ContainsKey(options.Voice.Id)) return await windows.GenerateAsync(text, options with { Voice = null }, token);
        try { return await piper.GenerateAsync(text, options, token); }
        catch (Exception e) when (e is not OperationCanceledException && !token.IsCancellationRequested)
        {
            if (failedVoices.TryAdd(options.Voice.Id, 0)) Notice?.Invoke("Piper indisponível; usando a voz do Windows nesta sessão. Verifique ou repare a voz em Vozes offline.");
            return await windows.GenerateAsync(text, options with { Voice = null }, token);
        }
    }
    public void Dispose() => piper.Dispose();
}
