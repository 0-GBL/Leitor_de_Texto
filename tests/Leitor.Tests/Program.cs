using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Leitor.Core;
using Leitor.Services;
using NAudio.Wave;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Leitor.ViewModels;

static class Program
{
    private static int passed;
    private static readonly List<string> failed = [];
    [STAThread]
    static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "Leitor-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Run("Offsets preservados, CRLF, Unicode e abreviações", () =>
        {
            const string text = "# Título\r\n\r\nO Dr. Silva lê. Tudo bem? Sim!\r\nAção e emoção 🦊.";
            var parts = TextProcessor.Split(text); Check(parts.Count >= 5, "Frases não identificadas");
            Check(parts[0].Heading, "Título não detectado"); Check(parts.Any(x => x.Text.Contains("Dr. Silva")), "Abreviação dividida");
            foreach (var s in parts) Check(text.Substring(s.Start, s.Length) == s.Text, "Original alterado");
        });
        Run("Frases gigantes limitam memória sem perder conteúdo", () =>
        {
            string text = new('a', 100_000); var result = TextProcessor.Split(text);
            Check(result.Count > 200 && result.All(x => x.Length <= 420), "Bloco grande demais"); Check(string.Concat(result.Select(x => x.Text)) == text, "Conteúdo perdido");
        });
        Run("Texto extenso, 3 milhões de caracteres", () =>
        {
            string text = string.Concat(Enumerable.Repeat("Um parágrafo com conteúdo. Outra frase para estudar.\r\n", 60_000));
            var timer = Stopwatch.StartNew(); long before = GC.GetTotalAllocatedBytes(true);
            var result = TextProcessor.Split(text); timer.Stop();
            Check(result.Count == 120_000, "Contagem de frases incorreta");
            Console.WriteLine($"  {text.Length:N0} caracteres; {timer.ElapsedMilliseconds} ms; alocação {(GC.GetTotalAllocatedBytes(true) - before) / 1048576.0:0.0} MB");
        });
        Run("Cancelamento do processamento", () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { TextProcessor.Split("Texto.", cancel.Token); throw new Exception("Cancelamento ignorado"); } catch (OperationCanceledException) { }
        });
        Run("Pontuação: vírgulas, decimais, aspas, reticências e exclamações", () =>
        {
            const string original = "O Dr. Silva disse: “Sim, custa 3,50.” Depois, pensou... E concluiu: ‘A!’ Outra frase… Fim?";
            var s = TextProcessor.Split(original);
            Check(s.Count == 5, "Divisão incorreta: " + string.Join(" | ", s.Select(x => x.Text)));
            Check(s[0].Text.Contains("3,50.”") && s[1].Text.EndsWith("... ") && s[2].Text.Contains("‘A!’"), "Pontuação descartada");
            Check(string.Concat(s.Select(x => x.Text)) == original, "Original alterado");
            var longParts = TextProcessor.Split(new string('a', 450) + ", depois continua. Fim.");
            var options = new SpeechOptions(null, 1, 0, 1, 3, 5);
            Check(TextProcessor.Pause(longParts[0], options) == 0, "Pausa de frase inserida no meio de uma frase longa");
        });
        Run("Compatibilidade com JSON 1.0 sem novos campos de progresso", () =>
        {
            var legacy = JsonSerializer.Deserialize<Document>("{\"Title\":\"Antigo\",\"Content\":\"Original\",\"Position\":4,\"Offset\":1.2345,\"Protected\":false}")!;
            Check(legacy.Position == 4 && legacy.Offset == 1.2345 && legacy.ElapsedSeconds == null && legacy.SegmentDurations == null, "Documento antigo incompatível");
            const string legacyText = "Ela disse: “Sim.” Depois continuou. Fim.";
            Check(TextProcessor.SplitLegacy(legacyText)[0].Text == "Ela disse: “Sim.” Depois continuou. ", "Índices antigos mudaram");
            Check(TextProcessor.Split(legacyText)[0].Text == "Ela disse: “Sim.” ", "Nova pontuação não aplicada");
            var settings = JsonSerializer.Deserialize<Settings>("{\"Dark\":true,\"Speed\":1.5,\"Mode\":\"Rápida\",\"Background\":false}")!;
            Check(settings.Dark && !settings.Background && settings.Speed == 1.5 && settings.Mode == "Rápida", "Configuração antiga alterada");
        });
        Run("Download interrompido não publica modelo incompleto nem substitui a voz anterior", () =>
        {
            string voices = Path.Combine(root, "downloads"); Directory.CreateDirectory(voices);
            var model = VoiceDownloads.Catalog[0]; string target = Path.Combine(voices, model.Id + ".onnx");
            File.WriteAllText(target, "MODELO ANTERIOR"); File.WriteAllText(target + ".json", "CONFIG ANTERIOR");
            using var http = new HttpClient(new BrokenVoiceHandler());
            try { new VoiceDownloads(http).DownloadAsync(model, "Ficha de teste", voices, null, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Configuração inválida aceita"); }
            catch (JsonException) { }
            Check(File.ReadAllText(target) == "MODELO ANTERIOR" && File.ReadAllText(target + ".json") == "CONFIG ANTERIOR", "Instalação anterior perdida");
            Check(!Directory.GetFiles(voices, "*.part").Any(), "Restos de download mantidos");
        });
        Run("Download recupera falha HTTP transitória sem deixar arquivos parciais", () =>
        {
            var handler = new RetryingVoiceHandler(); using var http = new HttpClient(handler);
            string folder = Path.Combine(root, "download-retry"); var model = VoiceDownloads.Catalog[0];
            new VoiceDownloads(http).DownloadAsync(model, "Ficha de teste", folder, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(handler.Calls == 3 && File.Exists(Path.Combine(folder, model.Id + ".onnx.sha256")), "Não repetiu o download");
            Check(!Directory.GetFiles(folder, "*.part").Any(), "Parciais não foram removidos");
        });
        Run("Pausas Estudos e estimativa", () =>
        {
            var o = new SpeechOptions(null, .9, 0, 1, 3, 5); var s = TextProcessor.Split("# Título\nFrase um. Frase dois.");
            Check(TextProcessor.Pause(s[0], o) == 5 && TextProcessor.Pause(s[1], o) == 1 && TextProcessor.Pause(s[2], o) == 3, "Pausas incorretas");
        });
        Run("SQLite: conteúdo, etiquetas e retomada após reabrir", () =>
        {
            using var vault = new Vault(root); var store = new LibraryStore(root, vault);
            var d = new Document { Title = "Faculdade", Content = "Original\r\nsem alterações.", Tags = "faculdade, banco de dados", Position = 7, Offset = 2.3 }; store.Save(d);
            var reopened = new LibraryStore(root, vault).Load().Single();
            Check(reopened.Content == d.Content && reopened.Tags == d.Tags && reopened.Position == 7 && reopened.Offset == 2.3, "Persistência incorreta");
            store.Delete(d.Id); Check(store.Load().Count == 0, "Exclusão falhou");
        });
        Run("PIN, AES-GCM, adulteração e dados ausentes no SQLite", () =>
        {
            using var vault = new Vault(root); vault.Create("938271"); var store = new LibraryStore(root, vault);
            var doc = new Document { Title = "TITULO_ULTRASSECRETO", Content = "SEGREDO_7d88a3c3", Tags = "ETIQUETA_SECRETA", Protected = true, Position = 4 }; store.Save(doc);
            string disk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "library.db")));
            Check(!disk.Contains(doc.Content) && !disk.Contains(doc.Title) && !disk.Contains(doc.Tags), "Dados em texto simples");
            byte[] sealedValue = vault.Seal(Encoding.UTF8.GetBytes("teste")); sealedValue[^1] ^= 1;
            try { vault.Open(sealedValue); throw new Exception("Adulteração aceita"); } catch (CryptographicException) { }
            vault.Lock(); Check(store.Load().Single().Content == "", "Texto disponível bloqueado");
            Check(!vault.Unlock("000000"), "PIN incorreto aceito"); Check(vault.Unlock("938271"), "PIN correto recusado");
            Check(store.Load().Single().Content == doc.Content, "Falha ao descriptografar");
            store.DisableProtection(); Check(!vault.Configured && !store.Load().Single().Protected, "Desativação falhou");
            store.Delete(doc.Id);
        });
        Run("Configurações com padrões offline e segurança desligada", () =>
        {
            var store = new SettingsStore(root); var settings = store.Load(); Check(settings.Background && !settings.AppLock, "Padrões incorretos");
            settings.Dark = true; settings.Mode = "Estudos"; store.Save(settings); Check(store.Load().Dark && store.Load().Mode == "Estudos", "Configurações não persistidas");
        });
        Run("Motor Windows gera WAV local e cancelável", () =>
        {
            var voices = WindowsSpeechEngine.Voices(); Check(voices.Count > 0, "Nenhuma voz SAPI instalada");
            Console.WriteLine("  " + string.Join(", ", voices.Select(x => x.Name)));
            var options = new SpeechOptions(voices[0], 1, 0, 0, 0, 0);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            byte[] bytes = new WindowsSpeechEngine().GenerateAsync("Olá. Teste de leitura local.", options, timeout.Token).GetAwaiter().GetResult();
            using var wav = new WaveFileReader(new MemoryStream(bytes)); Check(wav.TotalTime.TotalSeconds > .5, "Áudio vazio");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { new WindowsSpeechEngine().GenerateAsync("Teste", options, canceled.Token).GetAwaiter().GetResult(); throw new Exception("Cancelamento ignorado"); } catch (OperationCanceledException) { }
        });
        Run("Dispositivo de áudio e leitura progressiva: pausa, retomada, troca e cancelamento", () =>
        {
            Console.WriteLine($"  dispositivos de saída: {WaveOut.DeviceCount}");
            var engine = new WindowsSpeechEngine(); using var player = new Playback(engine);
            string? error = null; player.Error += e => error = e;
            player.SetVolume(0);
            var parts = TextProcessor.Split("Uma frase longa para testar a reprodução progressiva de áudio e o cancelamento. Outra frase para testar a fila.\nUm segundo parágrafo.");
            player.LoadAsync(parts).GetAwaiter().GetResult(); player.StartAsync().GetAwaiter().GetResult();
            Thread.Sleep(900); Check(player.Playing, error ?? "Leitura encerrou cedo");
            player.TogglePause(); Check(player.Paused, "Não pausou"); double offset = player.Offset;
            Thread.Sleep(250); Check(Math.Abs(player.Offset - offset) < .3, "Avançou enquanto pausado");
            player.TogglePause(); Check(!player.Paused, "Não retomou");
            player.SetOptions(new(null, 1.5, 0, .1, .2, .3), "Ao final da frase");
            player.RestartAsync(1).GetAwaiter().GetResult(); Thread.Sleep(300);
            player.StopAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            Check(!player.Playing && error == null, error ?? "Não parou");
        });
        Run("Cursor audível, retomada em 01:12 e reinício desde zero", () =>
        {
            var parts = TextProcessor.Split("Primeira frase. Segunda frase. Terceira frase. Quarta frase.");
            using var player = new Playback(new SilentEngine()); player.SetVolume(0);
            var known = new Dictionary<int, double> { [parts[0].Start] = 10, [parts[1].Start] = 25, [parts[2].Start] = 36 };
            player.LoadAsync(parts, known).GetAwaiter().GetResult();
            player.RestoreAsync(3, 1.275, 72.275).GetAwaiter().GetResult();
            Check(Math.Abs(player.Position - 72.275) < .001, "Tempo restaurado perdeu precisão");
            player.StartAsync(3, 1.275).GetAwaiter().GetResult(); Thread.Sleep(180); player.TogglePause();
            Check(player.AudibleIndex == 3, "Frase audível incorreta");
            double position = player.Position, offset = player.Offset;
            Thread.Sleep(200); Check(Math.Abs(player.Offset - offset) < .01, "Cursor avançou durante pausa");
            player.StopAsync().GetAwaiter().GetResult(); Check(Math.Abs(player.Position - position) < .01, "Stop perdeu o deslocamento capturado");
            using var vault = new Vault(root); var store = new LibraryStore(root, vault);
            var doc = new Document { Content = "Progresso preciso", Position = player.Index, Offset = player.Offset, ElapsedSeconds = player.Position, CharacterPosition = parts[player.Index].Start, SegmentDurations = player.KnownDurations };
            store.Save(doc); var reopened = store.Load().Single();
            using var restored = new Playback(new SilentEngine()); restored.SetVolume(0);
            restored.LoadAsync(parts, reopened.SegmentDurations).GetAwaiter().GetResult(); restored.RestoreAsync(reopened.Position, reopened.Offset, reopened.ElapsedSeconds).GetAwaiter().GetResult();
            Check(Math.Abs(restored.Position - position) < .001, "Persistência arredondou o tempo");
            restored.StartAsync(reopened.Position, reopened.Offset).GetAwaiter().GetResult(); Thread.Sleep(80); restored.TogglePause();
            Check(restored.Position >= position && restored.Position - position < .3, "Retomada deslocou o áudio");
            restored.RestartFromBeginningAsync().GetAwaiter().GetResult(); Thread.Sleep(60); restored.TogglePause();
            Check(restored.Index == 0 && restored.Offset < .3 && restored.Position < .3, "Reiniciar não voltou ao começo");
            restored.StopAsync().GetAwaiter().GetResult(); store.Delete(doc.Id);
            Console.WriteLine($"  posição salva/restaurada: {position:0.000} s; erro de persistência < 1 ms");
        });
        Run("Navegação separa parágrafos e títulos e respeita os limites", () =>
        {
            var parts = TextProcessor.Split("# Primeiro título\nParágrafo inicial. Mais uma frase.\nSegundo parágrafo. Outra frase.\n# Segundo título\nÚltimo parágrafo.");
            using var player = new Playback(new SilentEngine()); player.SetVolume(0); player.LoadAsync(parts).GetAwaiter().GetResult();
            player.RestoreAsync(4, 1, null).GetAwaiter().GetResult();
            player.NavigateAsync(false, false).GetAwaiter().GetResult(); Check(player.Index == 1, "Anterior repetiu o próprio parágrafo");
            player.NavigateAsync(true, false).GetAwaiter().GetResult(); Check(player.Index == 3, "Próximo parágrafo incorreto");
            player.NavigateAsync(true, true).GetAwaiter().GetResult(); Check(player.Index == 5, "Próximo título incorreto");
            player.StopAsync().GetAwaiter().GetResult(); player.RestoreAsync(6, 1.234, null).GetAwaiter().GetResult();
            player.NavigateAsync(true, true).GetAwaiter().GetResult(); Check(player.Index == 6 && player.Offset == 1.234, "Navegação sem destino reiniciou a frase");
        });
        Run("WPF: temas, configurações, modos, destaque independente e reiniciar", () => VerifyInterface(root));
        if (Environment.GetEnvironmentVariable("LEITOR_TEST_DOWNLOADS") == "1")
            Run("Download real de Faber e Edresson pelo mesmo serviço da interface", () =>
            {
                var service = new VoiceDownloads();
                foreach (var model in VoiceDownloads.Catalog.Where(x => x.Id.StartsWith("pt_BR")))
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    string license = service.LicenseAsync(model, timeout.Token).GetAwaiter().GetResult();
                    service.DownloadAsync(model, license, AppPaths.Voices, null, timeout.Token).GetAwaiter().GetResult();
                    Check(File.Exists(Path.Combine(AppPaths.Voices, model.Id + ".onnx.sha256")), "Modelo não publicado");
                }
            });
        if (PiperSpeechEngine.Installed && PiperSpeechEngine.Voices().Count > 0)
            Run("Piper persistente: duas frases neurais offline", () =>
            {
                using var engine = new PiperSpeechEngine();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                foreach (var voice in PiperSpeechEngine.Voices().Where(x => x.Language == "pt-BR"))
                foreach (var sentence in new[] { "Olá, esta é uma leitura com voz neural.", "A segunda frase usa o mesmo processo local." })
                {
                    var timer = Stopwatch.StartNew(); var bytes = engine.GenerateAsync(sentence, new(voice, 1, 0, 0, 0, 0), timeout.Token).GetAwaiter().GetResult();
                    using var wav = new WaveFileReader(new MemoryStream(bytes)); Check(wav.TotalTime.TotalSeconds > .3, "Áudio vazio"); Console.WriteLine($"  {voice.Id}: geração {timer.ElapsedMilliseconds} ms; áudio {wav.TotalTime.TotalSeconds:0.0} s");
                }
            });
        else Console.WriteLine("SKIP: Piper ainda não instalado para teste neural.");
        Console.WriteLine($"RESULTADO: {passed} passaram, {failed.Count} falharam.");
        foreach (string f in failed) Console.WriteLine(f);
        // Only remove the isolated directory created above.
        Directory.Delete(root, true); return failed.Count == 0 ? 0 : 1;
    }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed.Add("FAIL: " + name + " — " + e); } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Tempo esgotado aguardando WPF.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void AwaitUI(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void VerifyInterface(string root)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Console.WriteLine("  WPF: carregando recursos");
        if (Application.Current == null) { var app = new Leitor.App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown; }
        Console.WriteLine("  WPF: criando ViewModel");
        var empty = new Leitor.MainWindow(new MainViewModel(Path.Combine(root, "empty-interface")));
        empty.Show(); PumpUntil(() => empty.IsLoaded); empty.Close(); PumpUntil(() => !empty.IsVisible);
        var vm = new MainViewModel(Path.Combine(root, "interface"));
        Console.WriteLine("  WPF: abrindo janela");
        var window = new Leitor.MainWindow(vm); window.Show();
        try
        {
            PumpUntil(() => window.IsLoaded); AwaitUI(vm.InitializeAsync());
            Check(((SolidColorBrush)Application.Current!.Resources["BackgroundBrush"]).Color.ToString() == "#FFF5EFE4", "Paleta clara incorreta");
            vm.ToggleThemeCommand.Execute(null);
            Check(vm.ThemeActionLabel.Contains("Tema claro") && ((SolidColorBrush)Application.Current.Resources["BackgroundBrush"]).Color.ToString() == "#FF111925", "Tema escuro/toggle incorretos");
            var settings = new Leitor.SettingsWindow(vm) { Owner = window }; settings.Show(); PumpUntil(() => settings.IsLoaded); settings.Close();
            var mode = (ComboBox)window.FindName("ModeSelector"); mode.SelectedItem = "Estudos";
            Check(vm.Settings.Speed == .9 && vm.Settings.SentencePause == 1 && vm.Settings.ParagraphPause == 3 && vm.Settings.HeadingPause == 5, "Modo Estudos alterado pelos sliders");
            mode.SelectedItem = "Rápida"; Check(vm.Settings.Speed == 1.5 && vm.Settings.SentencePause == .1, "Modo Rápida incorreto");
            mode.SelectedItem = "Natural"; Check(vm.Settings.Speed == 1 && vm.Settings.ParagraphPause == .65, "Modo Natural incorreto");
            vm.Settings.Volume = 0; vm.Content = "Primeira frase para acompanhar a leitura durante algum tempo. Outra frase para testar a seleção e o destaque.\nNovo parágrafo.";
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.NarratedSegment != null || vm.Status.StartsWith("Erro de áudio"));
            Check(vm.NarratedSegment != null, vm.Status); var narrated = vm.NarratedSegment!;
            vm.SelectedSegment = vm.Segments.Last(); Check(narrated.IsNarrating && !vm.SelectedSegment.IsNarrating, "Seleção desviou destaque da narração");
            vm.PlayCommand.Execute(null); Check(vm.Player.Paused, "Não pausou pela interface");
            vm.RestartCommand.Execute(null); PumpUntil(() => vm.Player.Playing && !vm.Player.Paused && vm.Player.Index == 0);
            Check(vm.Player.Offset < .4, "Reinício preservou offset antigo");
            PumpUntil(() => vm.Player.AudibleIndex == 0 && vm.Player.Offset > .2);
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.Player.Paused); // Freeze an exact hardware position for the close/reopen check.
            double beforeClose = vm.Player.Position;
            window.Close(); PumpUntil(() => !window.IsVisible);
            using var vault = new Vault(Path.Combine(root, "interface"));
            var restored = new LibraryStore(Path.Combine(root, "interface"), vault).Load().Single();
            Check(restored.ElapsedSeconds is double elapsed && Math.Abs(elapsed - beforeClose) < .03 && restored.CharacterPosition != null, "Fechar perdeu a posição final");
            Console.WriteLine($"  WPF: fechar/reabrir preservou {beforeClose:0.000} s");
        }
        finally
        {
            try { if (window.IsVisible) { window.Close(); PumpUntil(() => !window.IsVisible); } }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
    }
    private sealed class SilentEngine : ISpeechEngine
    {
        public Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); using var stream = new MemoryStream();
            using (var writer = new WaveFileWriter(new NonClosingStream(stream), new WaveFormat(16000, 16, 1))) writer.Write(new byte[16000 * 2 * 8], 0, 16000 * 2 * 8);
            return Task.FromResult(stream.ToArray());
        }
    }
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin s) => inner.Seek(o, s); public override void SetLength(long v) => inner.SetLength(v); public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
    }
    private sealed class BrokenVoiceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = request.RequestUri!.AbsolutePath.EndsWith(".json") ? new StringContent("inválido") : new ByteArrayContent(new byte[2048]) });
    }
    private sealed class RetryingVoiceHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (++Calls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = request.RequestUri!.AbsolutePath.EndsWith(".json") ? new StringContent("{\"audio\":{\"sample_rate\":16000}}") : new ByteArrayContent(new byte[2048]) });
        }
    }
}
