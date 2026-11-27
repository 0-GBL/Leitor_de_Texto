using System.IO;
using Leitor.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Leitor.Services;

public static class Mp3Export
{
    public const int BitRate = 96000;
    public const int SampleRate = 44100;

    public static string SuggestedName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new(title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        name = name.Trim().TrimEnd('.');
        if (name.Length > 100)
        {
            int length = char.IsHighSurrogate(name[99]) ? 99 : 100;
            name = name[..length].TrimEnd(' ', '.');
        }
        if (name.Length == 0) name = "Leitura";
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]))
            name = "_" + name;
        return name + ".mp3";
    }

    public static Task ExportAsync(string text, bool legacy, SpeechOptions options, string destination,
        IProgress<int> progress, CancellationToken token) =>
        Task.Run(() =>
        {
            using var piper = options.Voice?.Engine == "Piper" ? new PiperSpeechEngine() : null;
            Export(text, legacy, options, destination, (ISpeechEngine?)piper ?? new WindowsSpeechEngine(), progress, token);
        }, token);

    internal static void Export(string text, bool legacy, SpeechOptions options, string destination,
        ISpeechEngine engine, IProgress<int> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("O documento está vazio.");
        string finalPath = Path.GetFullPath(destination);
        if (!Path.GetExtension(finalPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha um destino com extensão .mp3.");
        if (File.Exists(finalPath)) throw new IOException("O arquivo já existe. Escolha outro nome.");
        var segments = legacy ? TextProcessor.SplitLegacy(text, token) : TextProcessor.Split(text, token);
        if (segments.Count == 0) throw new InvalidOperationException("O documento está vazio.");
        string scratch = Path.Combine(Path.GetDirectoryName(finalPath)!, ".leitor-" + Guid.NewGuid().ToString("N"));
        string partial = Path.Combine(scratch, "audio.mp3");
        try
        {
            AtomicFiles.EnsurePrivateDirectory(scratch);
            using (var provider = new Narration(segments, options, engine, progress, token))
                MediaFoundationEncoder.EncodeToMp3(provider, partial, BitRate);
            token.ThrowIfCancellationRequested();
            if (new FileInfo(partial).Length == 0) throw new IOException("O codificador não gerou áudio.");
            File.Move(partial, finalPath);
            progress.Report(100);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                File.Delete(partial);
                Directory.Delete(scratch);
            }
        }
    }

    private sealed class Narration(IReadOnlyList<Segment> segments, SpeechOptions options, ISpeechEngine engine,
        IProgress<int> progress, CancellationToken token) : IWaveProvider, IDisposable
    {
        public WaveFormat WaveFormat { get; } = new(SampleRate, 16, 1);
        private int segment, silence, reported = -1;
        private IWaveProvider? audio;
        private WaveFileReader? reader;

        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                token.ThrowIfCancellationRequested();
                if (audio != null)
                {
                    int read = audio.Read(buffer, offset + written, count - written);
                    written += read;
                    if (read > 0) continue;
                    Dispose();
                    silence = (int)Math.Round(TextProcessor.Pause(segments[segment], options) * SampleRate) * WaveFormat.BlockAlign;
                    segment++;
                }
                if (silence > 0)
                {
                    int length = Math.Min(count - written, silence);
                    Array.Clear(buffer, offset + written, length);
                    written += length;
                    silence -= length;
                    if (silence > 0) continue;
                }
                int percent = (int)((long)segment * 99 / segments.Count);
                if (reported != percent)
                {
                    reported = percent;
                    progress.Report(percent);
                }
                if (segment == segments.Count) break;
                var bytes = engine.GenerateAsync(segments[segment].Text, options, token).GetAwaiter().GetResult();
                reader = new WaveFileReader(new MemoryStream(bytes));
                ISampleProvider samples = reader.ToSampleProvider();
                if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
                if (samples.WaveFormat.Channels != 1) throw new InvalidDataException("O motor retornou áudio com canais incompatíveis.");
                if (samples.WaveFormat.SampleRate != SampleRate) samples = new WdlResamplingSampleProvider(samples, SampleRate);
                audio = new SampleToWaveProvider16(samples);
            }
            return written;
        }

        public void Dispose()
        {
            audio = null;
            reader?.Dispose();
            reader = null;
        }
    }
}
