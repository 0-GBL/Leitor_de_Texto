using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Leitor.Core;
using Leitor.Services;

namespace Leitor.ViewModels;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly SettingsStore settingsStore;
    private readonly LibraryStore library;
    public Vault Vault { get; }
    private readonly SpeechService speech = new();
    public Playback Player { get; }
    private readonly DispatcherTimer autosave, ticker;
    private readonly SemaphoreSlim storageGate = new(1, 1);
    private List<Document> all = [];
    private Document current = new();
    private bool dirty, loading, closed;
    private string title = "Novo texto", content = "", tags = "", search = "", tagFilter = "", status = "Pronto. Cole um texto para começar.";
    private Voice? voice;
    private Document? selectedDocument;
    private Segment? selectedSegment;
    private Segment? narratedSegment;
    private IReadOnlyList<Segment> parts = [];
    private bool isProtected;
    private int selectedTab;
    private int resumeIndex;
    private double resumeOffset;
    private double? resumeElapsed;
    private int? resumeCharacter;
    private Dictionary<int, double>? savedDurations;
    private bool legacySegmentation;
    private int narratedIndex = -1;
    private long revision;
    private CancellationTokenSource? processing;
    public Settings Settings { get; }
    public ObservableCollection<Document> Documents { get; } = [];
    public ObservableCollection<Voice> Voices { get; } = [];
    public IReadOnlyList<Segment> Segments => parts;
    public string[] Modes { get; } = ["Natural", "Rápida", "Estudos", "Personalizada"];
    public string[] Transitions { get; } = ["Imediata", "Ao final da frase", "Ao final do parágrafo"];
    public string Title { get => title; set { if (Set(ref title, value)) MarkDirty(); } }
    public string Content { get => content; set { if (Set(ref content, value)) { MarkDirty(); if (!loading) legacySegmentation = false; resumeIndex = 0; resumeOffset = 0; resumeElapsed = null; resumeCharacter = null; savedDurations = null; NarratedSegment = null; processing?.Cancel(); if (!loading) _ = Player.StopAsync(); parts = []; Raise(nameof(Segments)); } } }
    public string Tags { get => tags; set { if (Set(ref tags, value)) MarkDirty(); } }
    public string Search { get => search; set { if (Set(ref search, value)) Filter(); } }
    public string TagFilter { get => tagFilter; set { if (Set(ref tagFilter, value)) Filter(); } }
    public string Status { get => status; set => Set(ref status, value); }
    public bool IsProtected { get => isProtected; set { if (Set(ref isProtected, value)) { MarkDirty(); Raise(nameof(ProtectionLabel)); } } }
    public string ProtectionLabel => IsProtected ? "Este texto está protegido por PIN." : "Proteção deste texto desativada.";
    public Voice? SelectedVoice { get => voice; set { if (Set(ref voice, value)) { Settings.VoiceId = value?.Id; ApplySettings(); Raise(nameof(PitchSupported)); } } }
    public bool PitchSupported => SelectedVoice?.Engine == "Windows";
    public Document? SelectedDocument { get => selectedDocument; set => Set(ref selectedDocument, value); }
    public Segment? SelectedSegment { get => selectedSegment; set => Set(ref selectedSegment, value); }
    public Segment? NarratedSegment
    {
        get => narratedSegment;
        private set { if (narratedSegment == value) return; if (narratedSegment != null) narratedSegment.IsNarrating = false; narratedSegment = value; if (value != null) value.IsNarrating = true; Raise(); }
    }
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    public string PlayLabel => Player.Playing ? Player.Paused ? "▶ Continuar" : "Ⅱ Pausar" : "▶ Iniciar leitura";
    private double DisplayPosition => parts.Count == 0 || previewing ? resumeElapsed ?? 0 : Player.Position;
    private double DisplayDuration => parts.Count == 0 || previewing ? Math.Max(DisplayPosition, Content.Length / (14.0 * Settings.Speed)) : Player.Duration;
    public double Progress => DisplayDuration <= 0 ? 0 : Math.Clamp(DisplayPosition / DisplayDuration * 100, 0, 100);
    public double BufferedProgress => Player.Duration <= 0 ? 0 : Player.Buffered / Player.Duration * 100;
    public string TimeLabel => $"{Format(DisplayPosition)} / ~{Format(DisplayDuration)}  ·  {Progress:0}%";
    public string ProcessedLabel => $"Áudio preparado: {BufferedProgress:0}% · restante sob demanda";
    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss");
    public Command NewCommand { get; }
    public Command PasteCommand { get; }
    public AsyncCommand PlayCommand { get; }
    public AsyncCommand StopCommand { get; }
    public AsyncCommand RestartCommand { get; }
    public Command ToggleThemeCommand { get; }
    public string ThemeActionLabel => Settings.Dark ? "☀  Tema claro" : "☾  Tema escuro";
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand BackCommand { get; }
    public AsyncCommand ForwardCommand { get; }
    public AsyncCommand PreviousParagraphCommand { get; }
    public AsyncCommand NextParagraphCommand { get; }
    public AsyncCommand PreviousTopicCommand { get; }
    public AsyncCommand NextTopicCommand { get; }
    public AsyncCommand JumpCommand { get; }
    public AsyncCommand PreviewCommand { get; }
    public AsyncCommand ProtectionCommand { get; }
    public AsyncCommand PinCommand { get; }
    public event Action? ThemeChanged;
    public MainViewModel(string? dataRoot = null)
    {
        string root = dataRoot ?? AppPaths.Root;
        settingsStore = new(root); Settings = settingsStore.Load();
        Vault = new(root); library = new(root, Vault); Player = new(speech);
        Action<Exception> error = e => Status = e is OperationCanceledException ? "Operação cancelada." : "Não foi possível concluir: " + e.Message;
        NewCommand = new(() => Guard(async () => { await SaveAsync(); await Player.LoadAsync([]); SetDocument(new()); }));
        PasteCommand = new(() => { try { if (Clipboard.ContainsText()) { Content = Clipboard.GetText(); SelectedTab = 0; } } catch (Exception e) { error(e); } });
        PlayCommand = new(PlayAsync, error);
        StopCommand = new(async () => { processing?.Cancel(); CaptureProgress(); await Player.StopAsync(); await SaveAsync(); RefreshPlayback(); Status = "Leitura parada; progresso salvo."; }, error);
        RestartCommand = new(RestartReadingAsync, error);
        ToggleThemeCommand = new(() => { Settings.Dark = !Settings.Dark; Raise(nameof(ThemeActionLabel)); SavePreferences(); });
        SaveCommand = new(async () => { await SaveAsync(); Status = "Texto salvo na biblioteca local."; }, error);
        OpenCommand = new(OpenAsync, error); DeleteCommand = new(DeleteAsync, error);
        BackCommand = new(() => Player.SeekAsync(Player.Position - 10), error); ForwardCommand = new(() => Player.SeekAsync(Player.Position + 10), error);
        PreviousParagraphCommand = new(() => Player.NavigateAsync(false, false), error); NextParagraphCommand = new(() => Player.NavigateAsync(true, false), error);
        PreviousTopicCommand = new(() => Player.NavigateAsync(false, true), error); NextTopicCommand = new(() => Player.NavigateAsync(true, true), error);
        JumpCommand = new(async () => { if (SelectedSegment != null) await Player.RestartAsync(SelectedSegment.Index); }, error);
        PreviewCommand = new(async () => { await Player.StopAsync(); CaptureProgress(); await SaveAsync(); previewing = true; NarratedSegment = null; var preview = TextProcessor.Split("Olá! Esta é uma prévia da voz para suas leituras e estudos."); await Player.LoadAsync(preview); await Player.StartAsync(); Status = "Prévia da voz. Inicie a leitura para voltar ao texto."; }, error);
        ProtectionCommand = new(ToggleProtectionAsync, error); PinCommand = new(ConfigurePinAsync, error);
        Player.Error += message => Dispatch(() => Status = "Erro de áudio: " + message);
        speech.Notice += message => Dispatch(() => Status = message);
        Player.Changed += () => Dispatch(RefreshPlayback);
        autosave = new(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (_, _) => { if (dirty || Player.Playing && !previewing) await SaveSafelyAsync(); }, Application.Current.Dispatcher);
        ticker = new(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => { if (Player.Playing) RefreshPlayback(); }, Application.Current.Dispatcher);
        autosave.Start(); ticker.Start(); ReloadVoices();
    }
    private bool previewing;
    private async void Guard(Func<Task> task) { try { await task(); } catch (Exception e) { Status = e.Message; } }
    private void Dispatch(Action action) { if (!closed) Application.Current.Dispatcher.BeginInvoke(action); }
    private void MarkDirty() { if (!loading) { dirty = true; revision++; } }
    public async Task InitializeAsync()
    {
        if (Settings.AppLock && Vault.Configured && !SecurityDialog.Verify(Vault, "Desbloquear o Leitor")) { Application.Current.Shutdown(); return; }
        await ReloadLibraryAsync();
        var latest = all.FirstOrDefault(x => !x.Protected || Vault.Unlocked);
        if (latest != null) SetDocument(latest);
    }
    public void ReloadVoices()
    {
        string? selected = Settings.VoiceId; Voices.Clear();
        foreach (var v in PiperSpeechEngine.Voices().Concat(WindowsSpeechEngine.Voices())) Voices.Add(v);
        SelectedVoice = Voices.FirstOrDefault(x => x.Id == selected) ?? Voices.FirstOrDefault(x => x.Engine == "Piper") ?? Voices.FirstOrDefault();
        if (!PiperSpeechEngine.Installed) Status = "Voz do Windows pronta. Instale ou repare o Piper em Vozes offline para usar vozes neurais.";
    }
    public void ApplySettings()
    {
        Player.SetOptions(new(SelectedVoice, Settings.Speed, Settings.Pitch, Settings.SentencePause, Settings.ParagraphPause, Settings.HeadingPause), Settings.Transition);
        Player.SetVolume(Settings.Volume); SavePreferences();
    }
    public void SavePreferences() { settingsStore.Save(Settings); ThemeChanged?.Invoke(); Raise(nameof(ThemeActionLabel)); }
    public void SetMode()
    {
        switch (Settings.Mode)
        {
            case "Natural": Settings.Speed = 1; Settings.SentencePause = .25; Settings.ParagraphPause = .65; Settings.HeadingPause = 1; break;
            case "Rápida": Settings.Speed = 1.5; Settings.SentencePause = .1; Settings.ParagraphPause = .25; Settings.HeadingPause = .4; break;
            case "Estudos": Settings.Speed = .9; Settings.SentencePause = 1; Settings.ParagraphPause = 3; Settings.HeadingPause = 5; break;
        }
        Raise(nameof(Settings)); ApplySettings();
    }
    private async Task PlayAsync()
    {
        if (Player.Playing && !previewing) { Player.TogglePause(); return; }
        if (string.IsNullOrWhiteSpace(Content)) { Status = "Digite ou cole um texto antes de iniciar."; return; }
        await Player.StopAsync(); previewing = false;
        processing?.Cancel(); processing = new(); var token = processing.Token; string snapshot = Content;
        Status = "Organizando frases e parágrafos…";
        var split = await Task.Run(() => legacySegmentation ? TextProcessor.SplitLegacy(snapshot, token) : TextProcessor.Split(snapshot, token), token);
        token.ThrowIfCancellationRequested(); parts = split; narratedIndex = -1; NarratedSegment = null; Raise(nameof(Segments));
        ApplySettings(); await Player.LoadAsync(parts, savedDurations); SelectedTab = 1;
        if (resumeCharacter is int character)
            resumeIndex = parts.ToList().FindIndex(s => character >= s.Start && character < s.Start + s.Length);
        resumeIndex = Math.Clamp(resumeIndex, 0, Math.Max(0, parts.Count - 1));
        await Player.RestoreAsync(resumeIndex, resumeOffset, resumeElapsed);
        await Player.StartAsync(resumeIndex, resumeOffset);
        Status = "Leitura local · o áudio é preparado progressivamente.";
    }
    private async Task RestartReadingAsync()
    {
        if (string.IsNullOrWhiteSpace(Content)) { Status = "Digite ou cole um texto antes de reiniciar."; return; }
        processing?.Cancel(); await Player.StopAsync(); previewing = false;
        resumeIndex = 0; resumeOffset = 0; resumeElapsed = 0; resumeCharacter = null; legacySegmentation = false; NarratedSegment = null;
        // Rebuild the current document, so restarting never replays a voice preview.
        parts = []; savedDurations = null;
        await SaveAsync(); await PlayAsync();
        Status = "Leitura reiniciada desde o começo do texto.";
    }
    private void RefreshPlayback()
    {
        if (!previewing)
        {
            int audible = Player.AudibleIndex;
            if (narratedIndex != audible) { narratedIndex = audible; NarratedSegment = audible >= 0 && audible < parts.Count ? parts[audible] : null; }
        }
        if (!previewing && parts.Count > 0 && Player.Index >= parts.Count) { resumeIndex = 0; resumeOffset = 0; resumeElapsed = 0; resumeCharacter = null; }
        if (!previewing && Player.Playing) CaptureProgress();
        Raise(nameof(Progress)); Raise(nameof(BufferedProgress)); Raise(nameof(TimeLabel)); Raise(nameof(ProcessedLabel)); Raise(nameof(PlayLabel));
    }
    private void CaptureProgress()
    {
        if (previewing || parts.Count == 0) return;
        resumeIndex = Player.Index >= parts.Count ? 0 : Player.Index;
        resumeOffset = Player.Index >= parts.Count ? 0 : Player.Offset;
        resumeElapsed = Player.Index >= parts.Count ? 0 : Player.Position;
        resumeCharacter = Player.Index < parts.Count ? parts[Player.Index].Start : null;
    }
    private Document Snapshot() => new() { Id = current.Id, Title = string.IsNullOrWhiteSpace(Title) ? "Sem título" : Title.Trim(), Content = Content, Tags = Tags, Protected = IsProtected, Saved = DateTime.Now, Position = resumeIndex, Offset = resumeOffset, ElapsedSeconds = resumeElapsed, CharacterPosition = resumeCharacter, SegmentDurations = savedDurations, SegmentationVersion = legacySegmentation ? 1 : 2 };
    public async Task SaveAsync()
    {
        if (loading || string.IsNullOrWhiteSpace(Content) && !all.Any(x => x.Id == current.Id)) return;
        await storageGate.WaitAsync();
        try
        {
            CaptureProgress();
            if (!previewing && parts.Count > 0) savedDurations = Player.KnownDurations;
            var doc = Snapshot(); long savedRevision = revision;
            await Task.Run(() => library.Save(doc)); current = doc; if (savedRevision == revision) dirty = false;
            int index = all.FindIndex(x => x.Id == doc.Id); if (index >= 0) all[index] = doc; else all.Insert(0, doc);
            Filter();
        }
        finally { storageGate.Release(); }
    }
    private async Task SaveSafelyAsync() { try { await SaveAsync(); } catch (Exception e) { Status = "Falha no salvamento automático: " + e.Message; } }
    private async Task ReloadLibraryAsync() { all = await Task.Run(library.Load); Filter(); }
    private void Filter()
    {
        string? selected = SelectedDocument?.Id;
        Documents.Clear();
        foreach (var d in all.Where(x => (string.IsNullOrWhiteSpace(Search) || x.Title.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || x.Content.Contains(Search, StringComparison.CurrentCultureIgnoreCase)) && (string.IsNullOrWhiteSpace(TagFilter) || x.Tags.Split(',', StringSplitOptions.TrimEntries).Any(t => t.Equals(TagFilter.Trim(), StringComparison.CurrentCultureIgnoreCase))))) Documents.Add(d);
        SelectedDocument = Documents.FirstOrDefault(x => x.Id == selected);
    }
    private void SetDocument(Document document)
    {
        loading = true; current = document; narratedIndex = -1;
        Title = document.Title; Content = document.Content; Tags = document.Tags; IsProtected = document.Protected;
        resumeIndex = document.Position; resumeOffset = document.Offset; resumeElapsed = document.ElapsedSeconds;
        resumeCharacter = document.CharacterPosition; savedDurations = document.SegmentDurations;
        legacySegmentation = document.SegmentationVersion == 1 || document.SegmentationVersion == null && (document.Position > 0 || document.Offset > 0);
        parts = []; Raise(nameof(Segments)); SelectedSegment = null; NarratedSegment = null;
        loading = false; dirty = false; SelectedTab = 0;
        Status = document.Position > 0 || document.Offset > 0 ? "Progresso recuperado. Inicie para continuar de onde parou." : "Pronto para ler."; RefreshPlayback();
    }
    private async Task OpenAsync()
    {
        var target = SelectedDocument; if (target == null) return;
        await SaveAsync(); await Player.StopAsync(); previewing = false;
        if (target.Protected)
        {
            if (!SecurityDialog.Verify(Vault, "Abrir texto protegido")) return;
            await ReloadLibraryAsync(); target = all.First(x => x.Id == target.Id);
        }
        SetDocument(target);
    }
    private async Task DeleteAsync()
    {
        var target = SelectedDocument; if (target == null) return;
        if (target.Protected && !SecurityDialog.Verify(Vault, "Excluir texto protegido")) return;
        if (MessageBox.Show($"Excluir “{target.Title}” da biblioteca?", "Excluir texto", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Player.StopAsync(); await storageGate.WaitAsync();
        try { await Task.Run(() => library.Delete(target.Id)); if (current.Id == target.Id) SetDocument(new()); await ReloadLibraryAsync(); } finally { storageGate.Release(); }
    }
    private async Task ToggleProtectionAsync()
    {
        if (!Vault.Configured) { Status = "Configure um PIN antes de proteger textos."; return; }
        if (!SecurityDialog.Verify(Vault, IsProtected ? "Remover proteção deste texto" : "Proteger este texto")) return;
        IsProtected = !IsProtected; await SaveAsync(); Status = IsProtected ? "Título, texto, etiquetas e progresso criptografados." : "Proteção removida deste texto.";
    }
    private async Task ConfigurePinAsync()
    {
        if (!Vault.Configured)
        {
            string? pin = SecurityDialog.CreatePin(); if (pin == null) return;
            await Task.Run(() => Vault.Create(pin)); Settings.AppLock = true; ApplySettings(); Status = "PIN ativado para abrir o aplicativo. Proteja textos individualmente pelo botão Proteger.";
        }
        else
        {
            if (!SecurityDialog.Verify(Vault, "Desativar PIN e proteção dos textos")) return;
            if (MessageBox.Show("Desativar o PIN e descriptografar todos os textos protegidos?", "Desativar proteção", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await storageGate.WaitAsync();
            try { await Task.Run(library.DisableProtection); IsProtected = false; current.Protected = false; Settings.AppLock = false; ApplySettings(); await ReloadLibraryAsync(); } finally { storageGate.Release(); }
            Status = "PIN e criptografia desativados.";
        }
    }
    public async Task SeekPercentAsync(double value) { if (parts.Count > 0) await Player.SeekAsync(Player.Duration * value / 100); }
    public async Task ShutdownAsync()
    {
        autosave.Stop(); ticker.Stop(); processing?.Cancel(); await Player.StopAsync(); CaptureProgress(); await SaveAsync(); settingsStore.Save(Settings); closed = true; Dispose();
    }
    public void Dispose() { Player.Dispose(); speech.Dispose(); Vault.Dispose(); }
}
