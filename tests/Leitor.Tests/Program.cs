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
using Leitor.Presentation;
using Leitor.Security;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Windows.Input;

static partial class Program
{
    private static int passed;
    private static readonly List<string> failed = [];
    private static Exception? uiFault;
    private static string? testFilter;
    [STAThread]
    static int Main(string[] args)
    {
        testFilter = args.FirstOrDefault(x => x.StartsWith("--filter=", StringComparison.Ordinal))?[9..];
        if (args.Contains("--prepare-preview")) return PreparePreview();
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
        Run("Download repete HTTP transitório e rejeita hash divergente", () =>
        {
            var handler = new RetryingVoiceHandler(); using var http = new HttpClient(handler);
            string folder = Path.Combine(root, "download-retry"); var model = VoiceDownloads.Catalog[0];
            try { new VoiceDownloads(http).DownloadAsync(model, "Ficha de teste", folder, null, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Hash divergente foi aceito"); } catch (CryptographicException) { }
            Check(handler.Calls == 3 && !File.Exists(Path.Combine(folder, model.Id + ".onnx")), "Não repetiu ou publicou modelo inválido");
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
            var clock = new TestClock(); using var vault = new Vault(root, clock); vault.Create("938271"); var store = new LibraryStore(root, vault);
            var doc = new Document { Title = "TITULO_ULTRASSECRETO", Content = "SEGREDO_7d88a3c3", Tags = "ETIQUETA_SECRETA", Protected = true, Position = 4 }; store.Save(doc);
            string disk = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "library.db")));
            Check(!disk.Contains(doc.Content) && !disk.Contains(doc.Title) && !disk.Contains(doc.Tags), "Dados em texto simples");
            byte[] sealedValue = vault.Seal(Encoding.UTF8.GetBytes("teste")); sealedValue[^1] ^= 1;
            try { vault.Open(sealedValue); throw new Exception("Adulteração aceita"); } catch (CryptographicException) { }
            vault.Lock(); Check(store.Load().Single().Content == "", "Texto disponível bloqueado");
            Check(!vault.Unlock("000000"), "PIN incorreto aceito"); clock.Advance(31); Check(vault.Unlock("938271"), "PIN correto recusado");
            Check(store.Load().Single().Content == doc.Content, "Falha ao descriptografar");
            store.DisableProtection(); Check(!vault.Configured && !store.Load().Single().Protected, "Desativação falhou");
            store.Delete(doc.Id);
        });
        Run("Configurações com padrões offline e segurança desligada", () =>
        {
            var store = new SettingsStore(root); var settings = store.Load(); Check(settings.Background && !settings.AppLock, "Padrões incorretos");
            settings.Dark = true; settings.Mode = "Estudos"; store.Save(settings); Check(store.Load().Dark && store.Load().Mode == "Estudos", "Configurações não persistidas");
        });
        Run("PIN: espera persistente, alteração sem perda, nonces únicos e sessão", () =>
        {
            string folder = Path.Combine(root, "pin-session"); var clock = new TestClock();
            using var vault = new Vault(folder, clock); vault.Create("836492");
            byte[] encrypted = vault.Seal(Encoding.UTF8.GetBytes("Documento protegido"), "doc-A");
            byte[] another = vault.Seal(Encoding.UTF8.GetBytes("Documento protegido"), "doc-A");
            Check(!encrypted.SequenceEqual(another), "Nonce reutilizado");
            try { vault.Open(encrypted, "doc-B"); throw new Exception("Identidade não autenticada"); } catch (CryptographicException) { }
            vault.Lock(); Check(!vault.Unlock("000000"), "PIN inválido aceito");
            using (var restarted = new Vault(folder, clock)) { Check(restarted.RemainingLockout > TimeSpan.Zero && !restarted.Unlock("836492"), "Reiniciar removeu a espera"); }
            clock.Advance(31); Check(vault.Unlock("836492"), "PIN recusado depois da espera");
            vault.ChangePin("927463"); vault.Lock();
            Check(!vault.Unlock("836492"), "PIN antigo continuou válido"); clock.Advance(31);
            Check(vault.Unlock("927463") && Encoding.UTF8.GetString(vault.Open(encrypted, "doc-A")) == "Documento protegido", "Alterar PIN perdeu a chave");
            vault.Lock(); try { vault.Open(encrypted, "doc-A"); throw new Exception("Sessão bloqueada expôs dados"); } catch (InvalidOperationException) { }
            Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "auth-state.bin"))).Contains("927463"), "PIN em texto simples");
        });
        Run("SQLite: payload antigo, SQL Injection e adulteração por troca de identidade", () =>
        {
            string folder = Path.Combine(root, "sql-audit"); using var vault = new Vault(folder); vault.Create("932847");
            var library = new LibraryStore(folder, vault);
            var doc = new Document { Id = "x'); DROP TABLE documents; --", Content = "Segredo de teste", Protected = true };
            byte[] legacy = vault.Seal(JsonSerializer.SerializeToUtf8Bytes(doc));
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "library.db"), Pooling = false }.ToString()); db.Open();
            using (var cmd = db.CreateCommand()) { cmd.CommandText = "INSERT INTO documents VALUES($id,1,$p)"; cmd.Parameters.AddWithValue("$id", doc.Id); cmd.Parameters.AddWithValue("$p", legacy); cmd.ExecuteNonQuery(); }
            Check(library.Load().Single().Content == doc.Content, "Criptografia antiga incompatível");
            library.Save(doc); Check(library.Load().Single().Content == doc.Content, "Parâmetros SQL alteraram a consulta");
            using (var cmd = db.CreateCommand()) { cmd.CommandText = "UPDATE documents SET id='outra-identidade'"; cmd.ExecuteNonQuery(); }
            Check(library.Load().Single().IsUnavailable, "Payload transferido para outra linha foi aceito");
            try { library.DisableProtection(); throw new Exception("Integridade inválida removeu a chave"); } catch (InvalidOperationException) { }
            Check(vault.Configured, "Falha de integridade apagou a chave");
        });
        Run("Configurações inválidas são normalizadas e arquivo corrompido preservado", () =>
        {
            string folder = Path.Combine(root, "bad-settings"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "settings.json"), "{\"Speed\":-10,\"Volume\":999,\"FontSize\":-1}");
            var store = new SettingsStore(folder); var config = store.Load(); Check(config.Speed == .5 && config.Volume == 100 && config.FontSize == 14, "Faixas não verificadas");
            File.WriteAllText(Path.Combine(folder, "settings.json"), "incompleto"); Check(!store.Load().AppLock && Directory.GetFiles(folder, "settings.json.invalid-*").Length == 1, "Arquivo inválido perdido");
        });
        Run("Extração de pacotes rejeita Zip Slip, caminhos absolutos e links", () =>
        {
            foreach (string name in new[] { "../fora.txt", "/absoluto.txt", "C:/fora.txt", "pasta/../../fora.txt" })
                try { SafeArchive.Resolve(Path.Combine(root, "archive"), name); throw new Exception("Caminho aceito: " + name); } catch (InvalidDataException) { }
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true)) { using var writer = new StreamWriter(zip.CreateEntry("pasta/ok.txt").Open()); writer.Write("ok"); }
            data.Position = 0; using var read = new ZipArchive(data, ZipArchiveMode.Read); SafeArchive.Extract(read, Path.Combine(root, "archive"));
            Check(File.ReadAllText(Path.Combine(root, "archive", "pasta", "ok.txt")) == "ok", "Pacote válido rejeitado");
        });
        Run("Atalhos contextuais preservam edição e cobrem ABNT2, navegação e ajuda", () =>
        {
            Check(Shortcuts.Resolve(Key.Space, ModifierKeys.None, true) == null && Shortcuts.Resolve(Key.V, ModifierKeys.Control, true) == null, "Edição de espaço/colar capturada");
            foreach (var key in new[] { Key.Left, Key.Right, Key.Up, Key.Down })
            { Check(Shortcuts.Resolve(key, ModifierKeys.Control, true) == null, "Movimento do cursor capturado"); Check(Shortcuts.Resolve(key, ModifierKeys.Control | ModifierKeys.Shift, true) == null, "Seleção do cursor capturada"); }
            Check(Shortcuts.Resolve(Key.Space, ModifierKeys.None, false) == ShortcutAction.Play, "Espaço fora da edição indisponível");
            Check(Shortcuts.Resolve(Key.Space, ModifierKeys.None, false, true) == null, "Espaço nativo de botão capturado");
            Check(Shortcuts.Resolve(Key.OemComma, ModifierKeys.Control, false) == ShortcutAction.Settings, "Ctrl+vírgula indisponível");
            Check(Shortcuts.Resolve(Key.Up, ModifierKeys.Control | ModifierKeys.Shift, false) == ShortcutAction.PreviousHeading, "Navegação de títulos indisponível");
            Check(Shortcuts.Resolve(Key.F1, ModifierKeys.None, true) == ShortcutAction.Help && Shortcuts.Resolve(Key.T, ModifierKeys.Control, true) == ShortcutAction.Theme, "Ajuda/tema indisponíveis");
            Check(Shortcuts.Resolve(Key.Enter, ModifierKeys.None, true, false, true) == null && Shortcuts.Resolve(Key.Enter, ModifierKeys.None, false, false, true) == ShortcutAction.Jump, "Enter fora do contexto");
            Check(Shortcuts.Resolve(Key.Delete, ModifierKeys.Control, false) == null, "Atalho destrutivo criado");
        });
        Run("Timeline: atualizações e consultas extensas equivalem à soma exata", () =>
        {
            var values = Enumerable.Range(0, 120_000).Select(i => 1.0 + i % 7).ToArray(); var timeline = new PlaybackTimeline(); timeline.Reset(values);
            timeline.Add(60_000, 3.5); values[60_000] += 3.5;
            Check(Math.Abs(timeline.Before(90_000) - values.Take(90_000).Sum()) < .0001 && Math.Abs(timeline.Total - values.Sum()) < .0001, "Tempo acumulado incorreto");
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
        Run("Pontuação 1.2.1: listas, datas, parênteses e títulos longos", () =>
        {
            const string original = "1. Primeiro item: data 17/11/2026 e valor 3,50.\n2. Segundo item (com observação).\n- Um item sem ponto\nO art. 5 e a pág. 12 foram lidos. Fim!";
            var parts = TextProcessor.Split(original);
            Check(parts[0].Text.StartsWith("1. Primeiro") && !parts[0].Heading, "Marcador numérico virou frase/título isolado");
            Check(parts.Any(x => x.Text.Contains("art. 5") && x.Text.Contains("pág. 12")), "Abreviação foi cortada");
            Check(parts.Any(x => x.Text.Contains("17/11/2026") && x.Text.Contains("3,50")), "Data/decimal alterados");
            Check(!parts.Single(x => x.Text.Contains("Um item sem ponto")).Heading, "Lista recebeu pausa de título");
            foreach (var s in parts) Check(original.Substring(s.Start, s.Length) == s.Text, "Original alterado");
            var heading = TextProcessor.Split("# " + new string('a', 900));
            Check(TextProcessor.Pause(heading[0], new(null, 1, 0, 1, 3, 5)) == 0, "Corte de título longo recebeu silêncio artificial");
        });
        Run("Timeline 1.2.1: busca logarítmica encontra limites exatos", () =>
        {
            var t = new PlaybackTimeline(); var durations = Enumerable.Range(0, 120_000).Select(i => 1d + i % 7).ToArray(); t.Reset(durations);
            foreach (int i in new[] { 0, 1, 20, 55_555, 119_999 })
            { double before = t.Before(i); Check(t.Locate(before + .1) == i, "Índice incorreto"); if (i > 0) Check(t.Locate(before - .001) == i - 1, "Limite incorreto"); }
        });
        Run("Seek: parado não reproduz; pausa, posição medida e destaque são preservados", () =>
        {
            var engine = new ControlledEngine(); using var player = new Playback(engine); player.SetVolume(0); player.SetOptions(new(null, 1, 0, 0, 0, 0), "Ao final da frase");
            var parts = TextProcessor.Split("Primeira frase. Segunda frase. Terceira frase. Quarta frase.");
            player.LoadAsync(parts, parts.ToDictionary(s => s.Start, _ => 8d)).GetAwaiter().GetResult();
            player.SeekAsync(22).GetAwaiter().GetResult(); Check(!player.Playing && engine.Calls == 0 && player.Index == 2 && Math.Abs(player.Offset - 6) < .001, "Seek parado iniciou a voz ou mudou o destino");
            player.StartAsync(2, 6).GetAwaiter().GetResult(); WaitFor(() => player.AudibleIndex == 2); player.TogglePause();
            player.SeekAsync(9.25).GetAwaiter().GetResult(); WaitFor(() => !player.Preparing && player.AudibleIndex == 1);
            Check(player.Paused && Math.Abs(player.Position - 9.25) < .02, "Seek perdeu a pausa ou o tempo medido");
            double frozen = player.Offset; Thread.Sleep(100); Check(Math.Abs(player.Offset - frozen) < .01, "Áudio avançou pausado");
            player.TogglePause(); Thread.Sleep(80); Check(player.Position >= 9.25 && !player.Paused, "Continuação não partiu do destino"); player.StopAsync().GetAwaiter().GetResult();
        });
        Run("Seek estimado prepara somente o destino e ancora o tempo selecionado", () =>
        {
            var engine = new ControlledEngine(); using var player = new Playback(engine); player.SetVolume(0); player.SetOptions(new(null, 1, 0, 0, 0, 0), "Ao final da frase");
            var parts = TextProcessor.Split(string.Concat(Enumerable.Repeat("Uma frase para estudar com calma e atenção. ", 20)));
            player.LoadAsync(parts).GetAwaiter().GetResult(); player.StartAsync().GetAwaiter().GetResult(); WaitFor(() => player.AudibleIndex == 0); player.TogglePause();
            var location = player.Locate(player.Duration * .72); int calls = engine.Calls;
            Check(location.Estimated, "Destino deveria ser estimado"); player.SeekAsync(location).GetAwaiter().GetResult(); WaitFor(() => !player.Preparing && player.AudibleIndex == location.Index);
            Check(player.Paused && Math.Abs(player.Position - location.Position) < .03, "Calibração da duração deslocou o destino");
            Check(Math.Abs(player.Offset - location.Fraction * 8) < .02 && engine.Calls - calls == 1, "Preparação não respeitou a fração ou antecipou áudio durante a pausa"); player.StopAsync().GetAwaiter().GetResult();
        });
        Run("Seek no intervalo de pausa não volta ao fim do áudio ao continuar", () =>
        {
            var engine = new ControlledEngine(); using var player = new Playback(engine); player.SetVolume(0); player.SetOptions(new(null, 1, 0, 1, 3, 5), "Ao final da frase");
            var parts = TextProcessor.Split("Primeira frase. Segunda frase."); player.LoadAsync(parts, new Dictionary<int, double> { [parts[0].Start] = 9, [parts[1].Start] = 11 }).GetAwaiter().GetResult();
            player.StartAsync().GetAwaiter().GetResult(); WaitFor(() => player.AudibleIndex == 0); player.TogglePause(); player.SeekAsync(8.6).GetAwaiter().GetResult(); WaitFor(() => !player.Preparing && player.AudibleIndex == 0);
            Check(player.Paused && Math.Abs(player.Position - 8.6) < .03, "Não preparou a pausa selecionada"); player.TogglePause(); Thread.Sleep(100);
            Check(player.Position >= 8.6 && player.Position < 9.1, "Continuar retrocedeu ou saltou o intervalo selecionado"); player.StopAsync().GetAwaiter().GetResult();
        });
        Run("Transporte rápido: último destino vence sem fila de sínteses", () =>
        {
            var engine = new ControlledEngine(); using var player = new Playback(engine); player.SetVolume(0); player.SetOptions(new(null, 1, 0, 0, 0, 0), "Ao final da frase");
            var parts = TextProcessor.Split(string.Concat(Enumerable.Repeat("Frase para navegar. ", 100)));
            player.LoadAsync(parts, parts.ToDictionary(s => s.Start, _ => 8d)).GetAwaiter().GetResult(); player.StartAsync().GetAwaiter().GetResult(); WaitFor(() => player.AudibleIndex == 0); player.TogglePause(); int before = engine.Calls;
            var requests = Enumerable.Range(1, 100).Select(i => player.SeekAsync(i * 3.1)).ToArray(); Task.WhenAll(requests).GetAwaiter().GetResult(); WaitFor(() => !player.Preparing && player.AudibleIndex == 38);
            Check(player.Paused && Math.Abs(player.Position - 310) < .03 && engine.Calls - before <= 2, "Pedidos antigos executaram sínteses ou sobrescreveram o destino");
            player.SetOptions(new(null, 1.4, 0, 0, 0, 0), "Imediata"); WaitFor(() => player.Preparing); WaitFor(() => !player.Preparing); Check(player.Paused, "Troca imediata de velocidade retirou pausa");
            player.StopAsync().GetAwaiter().GetResult(); Check(!player.Playing, "Parada não prevaleceu");
        });
        Run("Falha de síntese é recuperável e cancelamento não bloqueia o transporte", () =>
        {
            var engine = new ControlledEngine { Fail = true }; using var player = new Playback(engine); player.SetVolume(0); string? error = null; player.Error += e => error = e;
            player.LoadAsync(TextProcessor.Split("Frase de teste. Outra frase.")).GetAwaiter().GetResult(); player.StartAsync(0, 1).GetAwaiter().GetResult(); WaitFor(() => !player.Playing);
            Check(error != null && !player.Preparing && player.Index == 0, "Falha encerrou/invalidou o player"); engine.Fail = false; error = null;
            player.StartAsync().GetAwaiter().GetResult(); WaitFor(() => player.AudibleIndex == 0); Check(error == null, "Não recuperou após falha"); player.StopAsync().GetAwaiter().GetResult();
            engine.DelayMs = 1500; engine.IgnoreCancellation = true; player.StartAsync().GetAwaiter().GetResult();
            var watch = Stopwatch.StartNew(); player.StopAsync().GetAwaiter().GetResult(); Check(watch.ElapsedMilliseconds < 500 && !player.Playing, "Síntese atrasada bloqueou a interface");
        });
        Run("Materiais de 1.000 a 50.000 palavras: original, processamento e navegação", () =>
        {
            foreach (int words in new[] { 1000, 5000, 10000, 25000, 50000 })
            {
                string text = string.Concat(Enumerable.Repeat("Uma leitura local ajuda a estudar com calma e atenção.\n", words / 10));
                var watch = Stopwatch.StartNew(); var parts = TextProcessor.Split(text); double split = watch.Elapsed.TotalMilliseconds;
                Check(parts.Count == words / 10 && parts.All(x => text.Substring(x.Start, x.Length) == x.Text), "Texto extenso alterado");
                using var player = new Playback(new ControlledEngine()); player.LoadAsync(parts).GetAwaiter().GetResult(); double target = player.Duration * .87;
                player.SeekAsync(target).GetAwaiter().GetResult(); Check(!player.Playing && Math.Abs(player.Position - target) < .01 && player.Index > parts.Count / 2, "Navegação extensa alterou destino");
                Console.WriteLine($"  {words:N0} palavras: segmentação {split:0.00} ms; {parts.Count} frases");
            }
        });
        Run("Piper com modelo inválido: fallback local Windows sem expor detalhes", () =>
        {
            string model = Path.Combine(root, "invalid-model.onnx"); File.WriteAllText(model, "modelo inválido de teste");
            using var service = new SpeechService(); string? notice = null; int notices = 0; service.Notice += value => { notice = value; notices++; };
            var options = new SpeechOptions(new("invalid", "Teste", "Piper", "pt-BR", model), 1, 0, 0, 0, 0);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            byte[] audio = service.GenerateAsync("Uma frase sintética para verificar a recuperação local.", options, timeout.Token).GetAwaiter().GetResult();
            using var wav = new WaveFileReader(new MemoryStream(audio)); Check(wav.TotalTime.TotalSeconds > .3 && notice != null && !notice.Contains(root), "Fallback falhou ou exibiu caminho técnico");
            byte[] next = service.GenerateAsync("A frase seguinte continua na voz local disponível.", options, timeout.Token).GetAwaiter().GetResult();
            Check(next.Length > 1000 && notices == 1, "Modelo inválido foi tentado novamente em cada frase");
        });
        RunLibraryTests(root);
        RunMp3Tests(root);
        Run("WPF: temas, configurações, modos, destaque independente e reiniciar", () => VerifyInterface(root));
        Run("WPF: barra acessível, teclado, prévia congelada, documentos e recuperação SQLite", () => VerifySeekingInterface(root));
        Run("WPF biblioteca 1.3: pastas, menus, edição, contadores e preferências",()=>VerifyLibraryInterface(root));
        Run("WPF biblioteca 2.0: navegação, grade, temas, recentes e modo foco com áudio",()=>VerifyExplorerInterface(root));
        Run("Biblioteca 2.0: novas preferências de grade e painel sobrevivem à gravação", () =>
        {
            var store = new SettingsStore(Path.Combine(root, "sidebar-preferences"));
            var settings = store.Load();
            Check(settings.LibraryGrid && settings.LibraryIconSize == 30 && !settings.LibrarySidebarCollapsed, "Padrões da biblioteca divergiram");
            settings.LibraryGrid = false;
            settings.LibrarySidebarCollapsed = true;
            settings.LibraryWidth = 333;
            store.Save(settings);
            var restored = store.Load();
            Check(!restored.LibraryGrid && restored.LibrarySidebarCollapsed && restored.LibraryWidth == 333, "Grade, recolhimento ou largura não persistiram");
        });
        if (Environment.GetEnvironmentVariable("LEITOR_TEST_DOWNLOADS") == "1")
            Run("Download real de Faber e Edresson pelo mesmo serviço da interface", () =>
            {
                var service = new VoiceDownloads();
                foreach (var model in VoiceDownloads.Catalog.Where(x => x.Id.StartsWith("pt_BR")))
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
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
        SqliteConnection.ClearAllPools(); Directory.Delete(root, true); return failed.Count == 0 ? 0 : 1;
    }
    private static void Run(string name, Action test) { if (testFilter != null && !name.Contains(testFilter, StringComparison.OrdinalIgnoreCase)) return; try { test(); passed++; Console.WriteLine("PASS: " + name); } catch (Exception e) { failed.Add("FAIL: " + name + " — " + e); } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static int PreparePreview()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LEITOR_DATA_DIR"))) return 2;
        if (File.Exists(Path.Combine(AppPaths.Root, "library.db"))) return 3;
        using var vault = new Vault(AppPaths.Root); var library = new LibraryStore(AppPaths.Root, vault);
        string content = "# Arquitetura em camadas\nUma arquitetura clara organiza responsabilidades e torna o software mais fácil de evoluir. Cada camada tem um papel bem definido.\n\n# A lógica do domínio\nAs regras de negócio devem ser compreensíveis, testáveis e independentes dos detalhes da interface. Assim, o mesmo conhecimento pode servir a diferentes experiências.\n\n# Persistência e cuidado com os dados\nO SQLite mantém a biblioteca no próprio computador. Salvamento automático e retomada de progresso ajudam a preservar o ritmo de estudo.\n\n# Uma leitura por vez\nOuça, faça uma pausa e volte quando quiser. O seu próximo aprendizado começa com uma boa pergunta.";
        library.Save(new() { Title = "Notas de produtividade", Tags = "trabalho, rotina", Content = "Aprender é transformar curiosidade em prática.", Completed = true, ProgressPercent = 100, ElapsedSeconds = 48, Saved = DateTime.Now.AddHours(-3) });
        vault.Create("173829"); library.Save(new() { Title = "Exemplo protegido", Content = "Somente dados sintéticos de teste.", Protected = true, Saved = DateTime.Now.AddHours(-1) }); vault.Lock();
        library.Save(new() { Title = "Arquitetura em camadas", Tags = "estudos, desenvolvimento", Content = content, ProgressPercent = 32, Saved = DateTime.Now });
        new SettingsStore(AppPaths.Root).Save(new Settings { Volume = 0 }); return 0;
    }
    private static void PumpUntil(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (uiFault != null) { var fault = uiFault; uiFault = null; throw new InvalidOperationException("Falha no dispatcher WPF.", fault); }
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
        EnsureTestApplication();
        Console.WriteLine("  WPF: criando ViewModel");
        var emptyModel = new MainViewModel(Path.Combine(root, "empty-interface"));
        Console.WriteLine("  WPF: ViewModel pronto; carregando XAML");
        var empty = new Leitor.MainWindow(emptyModel) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000 };
        Console.WriteLine("  WPF: XAML carregado; exibindo janela vazia");
        empty.Show(); Console.WriteLine("  WPF: Show retornou"); PumpUntil(() => empty.IsLoaded); empty.Close(); PumpUntil(() => !empty.IsVisible);
        var vm = new MainViewModel(Path.Combine(root, "interface"));
        Console.WriteLine("  WPF: abrindo janela");
        var window = new Leitor.MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000 }; window.Show();
        try
        {
            PumpUntil(() => window.IsLoaded); AwaitUI(vm.InitializeAsync());
            window.UpdateLayout();
            Check(((TextBox)window.FindName("EditorBox")).FontWeight == FontWeights.Normal, "Texto do editor herdou peso do cabeçalho");
            Check(window.SearchBox.ActualHeight is > 0 and < 55, "Padding duplicado no campo de busca");
            window.Width = 950; window.UpdateLayout();
            var libraryColumn = (ColumnDefinition)((LibraryPanel)window.FindName("Sidebar")).FindName("LibraryColumn");
            Check(libraryColumn.ActualWidth is >= 200 and <= 400, "Layout compacto não adaptou os painéis");
            vm.ShowReaderCommand.Execute(null);
            Check(((SolidColorBrush)Application.Current!.Resources["BackgroundBrush"]).Color.ToString() == "#FFF5EFE4", "Paleta clara incorreta");
            vm.ToggleThemeCommand.Execute(null);
            Check(vm.ThemeActionLabel.Contains("Tema claro") && ((SolidColorBrush)Application.Current.Resources["BackgroundBrush"]).Color.ToString() == "#FF111925", "Tema escuro/toggle incorretos");
            Check(new SettingsStore(Path.Combine(root, "interface")).Load().Dark, "Tema não foi persistido");
            bool helpOpened = false;
            var closeHelp = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Normal, (_, _) =>
            { var help = Application.Current.Windows.OfType<Leitor.HelpWindow>().FirstOrDefault(); if (help != null) { helpOpened = true; help.Close(); } }, Dispatcher.CurrentDispatcher);
            var helpKey = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F1) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            closeHelp.Start(); window.RaiseEvent(helpKey); closeHelp.Stop();
            Check(helpOpened && Shortcuts.Groups.Count == 5, $"F1 não abriu a central de ajuda ({Keyboard.Modifiers}, {helpKey.Key}, {helpKey.Handled})");
            Check(Leitor.MainWindow.IsEditing((DependencyObject)window.FindName("EditorBox")), "Editor não identificado pelo contexto dos atalhos");
            var settings = new Leitor.SettingsWindow(vm) { Owner = window }; settings.Show(); PumpUntil(() => settings.IsLoaded); settings.Close();
            var mode = (ComboBox)window.FindName("ModeSelector"); mode.SelectedItem = "Estudos";
            Check(vm.Settings.Speed == .9 && vm.Settings.SentencePause == 1 && vm.Settings.ParagraphPause == 3 && vm.Settings.HeadingPause == 5, "Modo Estudos alterado pelos sliders");
            mode.SelectedItem = "Rápida"; Check(vm.Settings.Speed == 1.5 && vm.Settings.SentencePause == .1, "Modo Rápida incorreto");
            mode.SelectedItem = "Natural"; Check(vm.Settings.Speed == 1 && vm.Settings.ParagraphPause == .65, "Modo Natural incorreto");
            vm.Settings.Volume = 0; vm.Content = "Primeira frase para acompanhar a leitura durante algum tempo. Outra frase para testar a seleção e o destaque.\nNovo parágrafo.";
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.NarratedSegment != null || vm.Status.StartsWith("Erro de áudio"));
            Check(vm.NarratedSegment != null, vm.Status); var narrated = vm.NarratedSegment!;
            double playingOffset = vm.Player.Offset;
            vm.ToggleThemeCommand.Execute(null); vm.ToggleThemeCommand.Execute(null);
            Check(vm.Player.Playing && !vm.Player.Paused && vm.Player.Offset >= playingOffset, "Tema interrompeu a reprodução ativa");
            vm.SelectedSegment = vm.Segments.Last(); Check(narrated.IsNarrating && !vm.SelectedSegment.IsNarrating, "Seleção desviou destaque da narração");
            vm.PlayCommand.Execute(null); Check(vm.Player.Paused, "Não pausou pela interface");
            double pausedOffset = vm.Player.Offset; string originalContent = vm.Content;
            for (int i = 0; i < 4; i++) vm.ToggleThemeCommand.Execute(null);
            Check(vm.Player.Paused && vm.Content == originalContent && Math.Abs(vm.Player.Offset - pausedOffset) < .01, "Tema interrompeu ou alterou a leitura");
            vm.RestartCommand.Execute(null); PumpUntil(() => vm.Player.Playing && !vm.Player.Paused && vm.Player.Index == 0);
            Check(vm.Player.Offset < .4, "Reinício preservou offset antigo");
            PumpUntil(() => vm.Player.AudibleIndex == 0 && vm.Player.Offset > .2);
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.Player.Paused); // Freeze an exact hardware position for the close/reopen check.
            double beforeClose = vm.Player.Position;
            window.Close(); PumpUntil(() => !window.IsVisible);
            using var vault = new Vault(Path.Combine(root, "interface"));
            var restored = new LibraryStore(Path.Combine(root, "interface"), vault).Load().Single();
            Console.WriteLine($"  fechamento: capturado={beforeClose:0.000}, salvo={restored.ElapsedSeconds:0.000}, offset={restored.Offset:0.000}");
            Check(restored.ElapsedSeconds is double elapsed && Math.Abs(elapsed - beforeClose) < .03 && restored.CharacterPosition != null, "Fechar perdeu a posição final");
            Console.WriteLine($"  WPF: fechar/reabrir preservou {beforeClose:0.000} s");
            var finishModel = new MainViewModel(Path.Combine(root, "completion-interface"));
            var finishWindow = new Leitor.MainWindow(finishModel) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000 }; finishWindow.Show();
            try
            {
                AwaitUI(finishModel.InitializeAsync()); finishModel.Settings.Volume = 0;
                finishModel.Content = "Fim."; finishModel.PlayCommand.Execute(null);
                PumpUntil(() => finishModel.ReadingState == "Leitura concluída"); AwaitUI(finishModel.SaveAsync());
                using var finishVault = new Vault(Path.Combine(root, "completion-interface"));
                var finished = new LibraryStore(Path.Combine(root, "completion-interface"), finishVault).Load().Single();
                Check(finished.Completed && finished.ProgressPercent == 100, "Conclusão não foi persistida na biblioteca");
            }
            finally { finishWindow.Close(); PumpUntil(() => !finishWindow.IsVisible); }
        }
        finally
        {
            try { if (window.IsVisible) { window.Close(); PumpUntil(() => !window.IsVisible); } }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
    }
    private static void WaitFor(Func<bool> done)
    {
        var watch = Stopwatch.StartNew(); while (!done()) { if (watch.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("Operação de áudio não concluiu."); Thread.Sleep(5); }
    }
    private sealed class ControlledEngine : ISpeechEngine
    {
        public int Calls; public int DelayMs = 80; public bool Fail, IgnoreCancellation;
        public async Task<byte[]> GenerateAsync(string text, SpeechOptions options, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); await Task.Delay(DelayMs, IgnoreCancellation ? CancellationToken.None : token);
            if (Fail) throw new IOException("Falha simulada do motor");
            return await new SilentEngine().GenerateAsync(text, options, IgnoreCancellation ? CancellationToken.None : token);
        }
    }
    private static void VerifySeekingInterface(string root)
    {
        var oldContext = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        EnsureTestApplication();
        var engine = new ControlledEngine(); var vm = new MainViewModel(Path.Combine(root, "seeking-interface"), engine); var window = new Leitor.MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000 }; window.Show();
        try
        {
            AwaitUI(vm.InitializeAsync()); vm.Settings.Volume = 0; vm.Title = "Documento A";
            vm.Content = string.Concat(Enumerable.Repeat("Uma frase para navegar e estudar com calma. ", 80));
            AwaitUI(vm.SeekPercentAsync(20)); Check(!vm.Player.Playing && engine.Calls == 0, "Barra antes de iniciar disparou síntese");
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.NarratedSegment != null); vm.PlayCommand.Execute(null); Check(vm.Player.Paused, "Não pausou");
            var slider = (SeekSlider)window.FindName("Timeline"); slider.Focus(); window.UpdateLayout(); double before = slider.Value;
            void Pointer(string method, params object[] values) => typeof(SeekSlider).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(slider, values);
            double span = slider.ActualWidth - 16; int calls = engine.Calls; double duration = vm.SeekDuration;
            Pointer("BeginPointer", 8 + span * .60, false); Check(Math.Abs(slider.Value - 60) < .001, "Clique direto aplicou salto padrão em vez da posição exata");
            Pointer("MovePointer", 8 + span * .35); vm.Raise(nameof(vm.Progress));
            Check(Math.Abs(slider.Value - 35) < .001 && engine.Calls == calls, "Arraste gerou síntese ou disputou com o temporizador");
            Pointer("EndPointer", 8 + span * .35); PumpUntil(() => !slider.IsInteracting && !vm.Player.Preparing);
            Check(vm.Player.Paused && Math.Abs(vm.Player.Position - duration * .35) < .05, "Fim do arraste perdeu destino/pausa");
            double cancelledPosition = vm.Player.Position;
            Pointer("BeginPointer", 8 + span * .80, false); slider.CancelInteraction();
            Check(Math.Abs(vm.Player.Position - cancelledPosition) < .01, "Cancelar arraste alterou o áudio"); before = slider.Value;
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider), 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Check(slider.IsInteracting && slider.Value > before && ((TextBlock)window.FindName("SeekPreviewText")).Text.Contains("Prévia"), $"Tecla não exibiu prévia ({Keyboard.Modifiers}, {slider.IsVisible}, {slider.IsInteracting}, {before}, {slider.Value})");
            double preview = slider.Value; vm.Raise(nameof(vm.Progress)); window.UpdateLayout(); Check(Math.Abs(slider.Value - preview) < .0001, "Tick automático disputou o cursor");
            slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider), 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            PumpUntil(() => !slider.IsInteracting && !vm.Player.Preparing); Check(vm.Player.Paused, "Teclado retomou áudio pausado");
            var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(slider)!;
            var range = (System.Windows.Automation.Provider.IRangeValueProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)!;
            for (int i = 0; i < 20; i++) range.SetValue(30 + i);
            PumpUntil(() => !slider.IsInteracting && !vm.Player.Preparing); Check(vm.Player.Paused && Math.Abs(vm.Progress - 49) < 2, "Acessibilidade não confirmou o último destino");
            AwaitUI(vm.SaveAsync()); string firstId = vm.Documents.Single().Id; double firstTime = vm.Player.Position;
            vm.NewCommand.Execute(null); PumpUntil(() => vm.Content == ""); vm.Title = "Documento B"; vm.Content = "Outro documento para testar o progresso independente. Mais uma frase."; AwaitUI(vm.SaveAsync());
            vm.SelectedDocument = vm.Documents.Single(x => x.Id == firstId); vm.OpenCommand.Execute(null); PumpUntil(() => vm.Title == "Documento A");
            Check(vm.ElapsedLabel != "00:00", "Troca de documentos perdeu progresso"); vm.PlayCommand.Execute(null); PumpUntil(() => vm.NarratedSegment != null); vm.PlayCommand.Execute(null);
            Check(Math.Abs(vm.Player.Position - firstTime) < .4 && vm.Player.Paused, "Outro documento contaminou o cursor");
            vm.Search = "Documento B"; PumpUntil(()=>!vm.LibraryBusy && vm.Documents.Count==1); Check(vm.Documents.Count == 1, "Busca por título falhou"); vm.Search = "navegar"; PumpUntil(()=>!vm.LibraryBusy && vm.Documents.Count==1); Check(vm.Documents.Count == 1, "Busca por conteúdo falhou"); vm.Search = "";
            vm.Tags = "estudo, teste"; AwaitUI(vm.SaveAsync()); vm.TagFilter = "estudo"; PumpUntil(()=>!vm.LibraryBusy && vm.Documents.Count==1); Check(vm.Documents.Count == 1, "Filtro de etiquetas falhou"); vm.TagFilter = "";
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "seeking-interface", "library.db"), Pooling = false }.ToString()))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TRIGGER fail_save BEFORE INSERT ON documents BEGIN SELECT RAISE(ABORT, 'simulated-full'); END;"; cmd.ExecuteNonQuery();
                vm.Title = "Título preservado após falha";
                try { AwaitUI(vm.SaveAsync()); throw new Exception("Falha de escrita ignorada"); } catch (SqliteException) { }
                Check(vm.Content.Contains("navegar") && vm.Title.StartsWith("Título preservado"), "Falha apagou o editor");
                window.Close(); PumpUntil(() => vm.Status.StartsWith("Não foi possível salvar antes de fechar"));
                Check(window.IsVisible && window.IsEnabled && vm.Content.Contains("navegar"), "Falha de fechamento descartou o editor");
                cmd.CommandText = "DROP TRIGGER fail_save;"; cmd.ExecuteNonQuery();
            }
            AwaitUI(vm.SaveAsync()); Check(vm.Documents.Any(x => x.Title == vm.Title), "Não recuperou gravação após falha");
            vm.Title = "Autosave recuperado após falha"; PumpUntil(() => vm.Documents.Any(x => x.Title == vm.Title));
            vm.PlayCommand.Execute(null); PumpUntil(() => vm.Player.Playing && vm.NarratedSegment != null);
            AwaitUI(vm.SuspendAsync()); Check(vm.Player.Paused && vm.Status.Contains("suspensão"), "Suspensão não preservou pausa/progresso");
        }
        finally { window.Close(); PumpUntil(() => !window.IsVisible); SynchronizationContext.SetSynchronizationContext(oldContext); }
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
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(double seconds) => now = now.AddSeconds(seconds);
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
