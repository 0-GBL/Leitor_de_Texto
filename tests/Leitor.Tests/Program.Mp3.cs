using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Leitor.Core;
using Leitor.Services;
using NAudio.Wave;

static partial class Program
{
    private static void RunMp3Tests(string root)
    {
        var voice = WindowsSpeechEngine.Voices().FirstOrDefault();
        var options = new SpeechOptions(voice, 1, 0, .25, .65, 1);
        const string text = "Primeira frase, com pausa. A segunda termina aqui!\n\nAgora começa outro parágrafo.";
        Run("MP3 Windows: texto curto, pontuação, parágrafos e decodificação", () =>
        {
            Check(voice != null, "Nenhuma voz Windows");
            MeasureExport(root, "windows", text, options);
        });
        if (PiperSpeechEngine.Installed && PiperSpeechEngine.Voices().FirstOrDefault() is {} piper)
            Run("MP3 Piper: narração local e decodificação", () => MeasureExport(root, "piper", text, options with { Voice = piper }));
        Run("MP3: destino existente, documento vazio e nome reservado", () =>
        {
            string path = Path.Combine(root, "existente.mp3");
            File.WriteAllText(path, "original");
            try { Mp3Export.ExportAsync(text, false, options, path, new ExportProgress(), default).GetAwaiter().GetResult(); throw new Exception("Sobrescreveu"); }
            catch (IOException) { }
            Check(File.ReadAllText(path) == "original", "Destino alterado");
            try { Mp3Export.ExportAsync(" ", false, options, path, new ExportProgress(), default).GetAwaiter().GetResult(); throw new Exception("Aceitou texto vazio"); }
            catch (InvalidOperationException) { }
            Check(Mp3Export.SuggestedName("CON.txt") == "_CON.txt.mp3" && Mp3Export.SuggestedName("Aula: 03") == "Aula_ 03.mp3", "Nome inseguro");
        });
        Run("MP3: cancelamento em andamento e falha de gravação simulada removem temporários", () =>
        {
            string path = Path.Combine(root, "interrompido.mp3");
            using var cancel = new CancellationTokenSource();
            var engine = new ExportEngine((_, token) => { cancel.Cancel(); return Task.FromCanceled<byte[]>(token); });
            try { Mp3Export.Export(text, false, options, path, engine, new ExportProgress(), cancel.Token); throw new Exception("Cancelamento ignorado"); }
            catch (OperationCanceledException) { }
            Check(!File.Exists(path) && !Directory.EnumerateDirectories(root, ".leitor-*").Any(), "Resíduo após cancelar");
            engine = new ExportEngine((_, _) => Task.FromException<byte[]>(new IOException("Disco cheio simulado", unchecked((int)0x80070070))));
            try { Mp3Export.Export(text, false, options, path, engine, new ExportProgress(), default); throw new Exception("Falha ignorada"); }
            catch (IOException) { }
            Check(!File.Exists(path) && !Directory.EnumerateDirectories(root, ".leitor-*").Any(), "Resíduo após falha");
        });
        Run("MP3: fluxos sintéticos de 20, 30, 40 e 60 minutos", () =>
        {
            byte[] wav = ExportTone();
            var settings = options with { SentencePause = .5, ParagraphPause = .5, HeadingPause = .5 };
            foreach (int minutes in new[] { 20, 30, 40, 60 })
            {
                int count = minutes * 24;
                int calls = 0;
                var engine = new ExportEngine((part, _) =>
                {
                    calls++;
                    Check(part.Contains(calls.ToString("D4"), StringComparison.Ordinal), "Trechos fora de ordem");
                    return Task.FromResult(wav);
                });
                string path = Path.Combine(root, $"long-{minutes}.mp3");
                var timer = Stopwatch.StartNew();
                Mp3Export.Export(string.Join("\n\n", Enumerable.Range(1, count).Select(n => $"Trecho número {n:D4}.")),
                    false, settings, path, engine, new ExportProgress(), default);
                double seconds = Mp3Duration(path);
                Check(calls == count && Math.Abs(seconds - minutes * 60) < .2, $"Duração {seconds:F3}, chamadas {calls}/{count}");
                Console.WriteLine($"  MP3 sintético {minutes} min: {timer.Elapsed.TotalSeconds:F1} s; {new FileInfo(path).Length:N0} bytes.");
            }
            Check(!Directory.EnumerateDirectories(root, ".leitor-*").Any(), "Resíduo após conclusão");
        });
        Run("MP3 WPF: proteção, posição original e interface responsiva", () => VerifyExportInterface(root));
    }

    private static void VerifyExportInterface(string root)
    {
        EnsureTestApplication();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string profile = Path.Combine(root, "export-ui");
        using var vault = new Vault(profile);
        var store = new LibraryStore(profile, vault);
        store.Save(new Document { Title = "Aula 03", Content = "Primeira frase. Segunda frase.", Offset = 2.4, ProgressPercent = 18 });
        var vm = new Leitor.ViewModels.MainViewModel(profile);
        var host = new Leitor.MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, Left = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        host.Show();
        try
        {
            AwaitUI(vm.InitializeAsync());
            var snapshot = vm.PrepareExport();
            Check(snapshot?.Title == "Aula 03" && snapshot.Text == "Primeira frase. Segunda frase.", "Exportação usou outro documento");
            var dialog = new Leitor.ExportWindow(snapshot!) { Owner = host, ShowActivated = false };
            dialog.Show();
            Check(VisualChildren<Button>(dialog).Any(button => Equals(button.Content, "Exportar")), "Ação de exportação ausente");
            dialog.Close();
            int ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => ticks++;
            timer.Start();
            try
            {
                AwaitUI(Task.Run(() => Mp3Export.Export(snapshot!.Text, false, snapshot.Options,
                    Path.Combine(root, "snapshot.mp3"),
                    new ExportEngine(async (_, _) => { await Task.Delay(150); return ExportTone(); }), new ExportProgress(), default)));
            }
            finally { timer.Stop(); }
            var saved = store.Load().Single();
            Check(ticks > 0 && saved.Offset == 2.4 && saved.ProgressPercent == 18, "Interface bloqueada ou progresso alterado");
            vm.Vault.Create("173829");
            vm.IsProtected = true;
            vm.Vault.Lock();
            bool prompted = false;
            var dismiss = AnswerDialog("Desbloquear para exportar áudio", window => { prompted = true; window.Close(); });
            try { Check(vm.PrepareExport() == null && prompted && !vm.Vault.Unlocked, "Exportou sem autorizar PIN"); }
            finally { dismiss.Stop(); }
            vm.IsProtected = false;
        }
        finally
        {
            host.Close();
            PumpUntil(() => !host.IsVisible);
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static byte[] ExportTone()
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(16000, 16, 1)))
            for (int i = 0; i < 32000; i++) writer.WriteSample((float)(.2 * Math.Sin(i * Math.PI / 20)));
        return stream.ToArray();
    }

    private static void MeasureExport(string root, string name, string text, SpeechOptions options)
    {
        string path = Path.Combine(root, name + ".mp3");
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long workingSet = process.WorkingSet64;
        var timer = Stopwatch.StartNew();
        var progress = new ExportProgress();
        Mp3Export.ExportAsync(text, false, options, path, progress, default).GetAwaiter().GetResult();
        process.Refresh();
        using var decoded = new MediaFoundationReader(path);
        var samples = decoded.ToSampleProvider();
        float[] buffer = new float[8192];
        int read;
        float peak = 0;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            for (int i = 0; i < read; i++) peak = Math.Max(peak, Math.Abs(buffer[i]));
        Check(progress.Value == 100 && Mp3Duration(path) > 2 && peak > .01f, "Áudio vazio ou finalização incompleta");
        Console.WriteLine($"  MP3 {name}: {timer.Elapsed.TotalSeconds:F2} s; {Mp3Duration(path):F2} s de áudio; {new FileInfo(path).Length:N0} bytes; CPU {(process.TotalProcessorTime-cpu).TotalSeconds:F2} s; variação WS {(process.WorkingSet64-workingSet)/1048576d:F1} MiB (processo do teste).");
    }

    private static double Mp3Duration(string path)
    {
        using var stream = File.OpenRead(path);
        long samples = 0;
        while (Mp3Frame.LoadFromStream(stream) is {} frame)
        {
            Check(frame.SampleRate == Mp3Export.SampleRate && frame.BitRate == Mp3Export.BitRate && frame.ChannelMode == ChannelMode.Mono,
                $"Formato real: {frame.SampleRate} Hz, {frame.BitRate} bit/s, {frame.ChannelMode}");
            samples += frame.SampleCount;
        }
        return samples / (double)Mp3Export.SampleRate;
    }

    private sealed class ExportProgress : IProgress<int>
    {
        public int Value { get; private set; }
        public void Report(int value) => Value = value;
    }
    private sealed class ExportEngine(Func<string, CancellationToken, Task<byte[]>> generate) : ISpeechEngine
    {
        public Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token) => generate(text, token);
    }
}
