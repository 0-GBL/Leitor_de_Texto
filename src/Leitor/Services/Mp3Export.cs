using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Leitor.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Leitor.Services;

public enum ExportStage { VoiceMissing, Model, Initialization, Synthesis, Pcm, Encoder, Destination }

public sealed class Mp3ExportException(ExportStage stage, string message, Exception? cause = null, int? segment = null)
    : IOException(message, cause)
{
    public ExportStage Stage { get; } = stage;
    public int? SegmentNumber { get; } = segment;
    public string Diagnostic => $"{Stage} · trecho {SegmentNumber?.ToString() ?? "—"} · HRESULT 0x{(InnerException?.HResult ?? HResult):X8}";
}

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
            ValidateVoice(options.Voice);
            using var piper = options.Voice?.Engine == "Piper" ? new PiperSpeechEngine() : null;
            try { Export(text, legacy, options, destination, (ISpeechEngine?)piper ?? new WindowsSpeechEngine(), progress, token); }
            catch (Mp3ExportException error) when (!token.IsCancellationRequested &&
                error.Stage is ExportStage.Model or ExportStage.Initialization or ExportStage.Synthesis)
            {
                RecordSynthesisFailure(error, options, legacy);
                throw;
            }
        }, token);

    private static void RecordSynthesisFailure(Mp3ExportException error, SpeechOptions options, bool legacy)
    {
        try
        {
            var piper = error.InnerException as PiperFailure;
            var exceptions = new List<object>();
            for (Exception? cause = error; cause != null; cause = cause.InnerException)
            {
                exceptions.Add(new
                {
                    Type = cause.GetType().FullName,
                    HResult = $"0x{cause.HResult:X8}",
                    Message = cause is Mp3ExportException or PiperFailure ? cause.Message : null,
                    Frames = new StackTrace(cause, false).GetFrames().Select(frame =>
                    {
                        var method = frame.GetMethod();
                        return $"{method?.DeclaringType?.FullName}.{method?.Name}";
                    })
                });
            }
            string directory = Path.Combine(AppPaths.Root, "diagnostics");
            AtomicFiles.EnsurePrivateDirectory(directory);
            AtomicFiles.Write(Path.Combine(directory, "mp3-synthesis.json"), JsonSerializer.SerializeToUtf8Bytes(new
            {
                Utc = DateTime.UtcNow,
                Stage = error.Stage.ToString(),
                Segment = error.SegmentNumber,
                Engine = options.Voice?.Engine ?? "Windows",
                Voice = options.Voice?.Id,
                options.Speed,
                LegacySegmentation = legacy,
                Exceptions = exceptions,
                Worker = piper == null ? null : new { piper.Operation, piper.WorkerType, piper.WorkerMessage, piper.WorkerFrames }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceWarning("Falha ao salvar o diagnóstico local de síntese MP3. HRESULT 0x{0:X8}", failure.HResult);
        }
    }

    internal static void ValidateVoice(Voice? voice)
    {
        if (voice?.Engine == "Piper")
        {
            if (voice.Model == null || !File.Exists(voice.Model) || !File.Exists(voice.Model + ".json"))
                throw new Mp3ExportException(ExportStage.VoiceMissing, $"A voz {voice.Name} está sem o modelo ou a configuração. Reinstale-a em Vozes offline ou selecione outra voz.");
            if (!PiperSpeechEngine.Installed)
                throw new Mp3ExportException(ExportStage.Initialization, "Os recursos de execução do Piper estão incompletos. Repare o Piper em Vozes offline.");
        }
        else if (voice != null)
        {
            List<Voice> installed;
            try { installed = WindowsSpeechEngine.Voices(); }
            catch (Exception e) { throw new Mp3ExportException(ExportStage.Initialization, "Não foi possível consultar as vozes do Windows.", e); }
            if (!installed.Any(item => item.Id == voice.Id))
                throw new Mp3ExportException(ExportStage.VoiceMissing, $"A voz {voice.Name} não está instalada ou habilitada no Windows.");
        }
    }

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
            {
                // Check the selected engine with the first real segment before starting the encoder.
                provider.Prime();
                try { MediaFoundationEncoder.EncodeToMp3(provider, partial, BitRate); }
                catch (Exception) when (token.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw; }
                catch (Exception e) when (e is not Mp3ExportException && e is not IOException && e is not UnauthorizedAccessException)
                {
                    if (IsStorageError(e)) throw new Mp3ExportException(ExportStage.Destination, StorageMessage(e), e);
                    throw new Mp3ExportException(ExportStage.Encoder, "O codificador MP3 do Windows falhou. Confira os componentes de mídia deste Windows.", e);
                }
            }
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

    internal static bool IsStorageError(Exception e) => (uint)e.HResult is 0x80070070 or 0x80070027 or 0x80070005 or 0x80030070 or 0x80070003 or 0x8007007B or 0x800700CE;

    internal static string StorageMessage(Exception e) => (uint)e.HResult switch
    {
        0x80070070 or 0x80070027 or 0x80030070 => "Falta espaço livre para concluir o MP3.",
        0x80070005 => "A conta atual não tem permissão para gravar no destino.",
        0x800700CE => "O caminho é longo demais. Escolha uma pasta ou nome mais curto.",
        0x80070003 or 0x8007007B => "O caminho de destino está inválido ou indisponível.",
        _ => "Não foi possível gravar no destino escolhido."
    };

    private sealed class Narration(IReadOnlyList<Segment> segments, SpeechOptions options, ISpeechEngine engine,
        IProgress<int> progress, CancellationToken token) : IWaveProvider, IDisposable
    {
        public WaveFormat WaveFormat { get; } = new(SampleRate, 16, 1);
        private int segment, silence, reported = -1;
        private IWaveProvider? audio;
        private WaveFileReader? reader;

        public void Prime() => LoadSegment();

        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                token.ThrowIfCancellationRequested();
                if (audio != null)
                {
                    int read;
                    try { read = audio.Read(buffer, offset + written, count - written); }
                    catch (Exception e) { throw PcmFailure(e); }
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
                LoadSegment();
            }
            return written;
        }

        private void LoadSegment()
        {
            token.ThrowIfCancellationRequested();
            byte[] bytes;
            try { bytes = engine.GenerateAsync(segments[segment].Text, options, token).GetAwaiter().GetResult(); }
            catch (Exception) when (token.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw; }
            catch (PiperFailure e) when (e.Model)
            {
                throw new Mp3ExportException(ExportStage.Model, "O Piper não conseguiu carregar o modelo selecionado. Repare ou reinstale essa voz.", e, segment + 1);
            }
            catch (PiperFailure e) when (e.Initialization)
            {
                throw new Mp3ExportException(ExportStage.Initialization, "O Piper foi iniciado, mas não respondeu. Repare o runtime em Vozes offline.", e, segment + 1);
            }
            catch (Exception e)
            {
                throw new Mp3ExportException(ExportStage.Synthesis, $"A voz selecionada falhou ao sintetizar o trecho {segment + 1}.", e, segment + 1);
            }
            try
            {
                reader = new WaveFileReader(new MemoryStream(bytes));
                if (reader.Length == 0) throw new InvalidDataException();
                ISampleProvider samples = reader.ToSampleProvider();
                if (samples.WaveFormat.Channels == 2) samples = new StereoToMonoSampleProvider(samples);
                if (samples.WaveFormat.Channels != 1) throw new InvalidDataException("O motor retornou áudio com canais incompatíveis.");
                if (samples.WaveFormat.SampleRate != SampleRate) samples = new WdlResamplingSampleProvider(samples, SampleRate);
                audio = new SampleToWaveProvider16(samples);
            }
            catch (Exception e) { throw PcmFailure(e); }
        }

        private Mp3ExportException PcmFailure(Exception cause) =>
            new(ExportStage.Pcm, $"A voz retornou áudio em formato incompatível no trecho {segment + 1}.", cause, segment + 1);

        public void Dispose()
        {
            audio = null;
            reader?.Dispose();
            reader = null;
        }
    }
}
