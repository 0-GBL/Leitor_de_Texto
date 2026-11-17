using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text.Json.Serialization;

namespace Leitor.Core;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Raise(name); return true; }
    public void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class Command(Action execute, Func<bool>? can = null) : ICommand
{
    public bool CanExecute(object? p) => can?.Invoke() ?? true;
    public void Execute(object? p) => execute();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
public sealed class AsyncCommand(Func<Task> execute, Action<Exception> error) : ICommand
{
    private bool busy;
    public bool CanExecute(object? p) => !busy;
    public async void Execute(object? p)
    { if (busy) return; busy = true; CommandManager.InvalidateRequerySuggested(); try { await execute(); } catch (Exception e) { error(e); } finally { busy = false; CommandManager.InvalidateRequerySuggested(); } }
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
public sealed record Segment(int Index, int Start, int Length, int Paragraph, bool Heading, bool ParagraphEnd, string Text)
    : INotifyPropertyChanged
{
    private bool narrating;
    public bool IsNarrating { get => narrating; set { if (narrating == value) return; narrating = value; PropertyChanged?.Invoke(this, new(nameof(IsNarrating))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label => $"{(Heading ? "Tópico" : "§ " + (Paragraph + 1))} · {Text.Trim().ReplaceLineEndings(" ")}";
}
public sealed record Voice(string Id, string Name, string Engine, string Language, string? Model = null)
{ public override string ToString() => $"{Name} · {Language} · {Engine}"; }
public sealed class Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Novo texto";
    public string Content { get; set; } = "";
    public string Tags { get; set; } = "";
    public DateTime Saved { get; set; } = DateTime.Now;
    public int Position { get; set; }
    public double Offset { get; set; }
    // Optional fields keep documents from 1.0 readable without a database migration.
    public int? CharacterPosition { get; set; }
    public double? ElapsedSeconds { get; set; }
    public Dictionary<int, double>? SegmentDurations { get; set; }
    public int? SegmentationVersion { get; set; }
    public bool Protected { get; set; }
    public bool Completed { get; set; }
    public double? ProgressPercent { get; set; }
    [JsonIgnore] public bool IsUnavailable { get; set; }
    [JsonIgnore] public bool IsLocked { get; set; }
    [JsonIgnore] public double ProgressValue => Completed ? 100 : Math.Clamp(ProgressPercent ?? (Content.Length > 0 && CharacterPosition is int p ? 100.0 * p / Content.Length : 0), 0, 100);
    [JsonIgnore] public string ProgressLabel => IsLocked ? "Leitura protegida" : Completed ? "Concluído" : $"{ProgressValue:0}% lido";
    [JsonIgnore] public string ProtectionStatus => IsUnavailable ? "Indisponível · integridade" : Protected ? "Protegido por PIN" : "Texto local";
    [JsonIgnore] public string DateLabel => IsLocked ? "Desbloqueie para ver os detalhes" : $"{Saved:dd MMM yyyy · HH:mm}";
    public string Description => Protected ? "Protegido por PIN" : $"{Saved:dd/MM/yyyy HH:mm}  ·  {Tags}";
}
public sealed record SpeechOptions(Voice? Voice, double Speed, double Pitch, double SentencePause, double ParagraphPause, double HeadingPause);
public sealed class Settings : Observable
{
    private bool dark, background = true, autoScroll = true, highlight = true, appLock;
    private double fontSize = 20, volume = 80, speed = 1, pitch, sentencePause = .25, paragraphPause = .65, headingPause = 1;
    private string mode = "Natural", transition = "Ao final da frase";
    private string? voiceId;
    public bool Dark { get => dark; set => Set(ref dark, value); }
    public bool Background { get => background; set => Set(ref background, value); }
    public bool AutoScroll { get => autoScroll; set => Set(ref autoScroll, value); }
    public bool Highlight { get => highlight; set => Set(ref highlight, value); }
    public double FontSize { get => fontSize; set => Set(ref fontSize, value); }
    public double Volume { get => volume; set => Set(ref volume, value); }
    public double Speed { get => speed; set => Set(ref speed, value); }
    public double Pitch { get => pitch; set => Set(ref pitch, value); }
    public double SentencePause { get => sentencePause; set => Set(ref sentencePause, value); }
    public double ParagraphPause { get => paragraphPause; set => Set(ref paragraphPause, value); }
    public double HeadingPause { get => headingPause; set => Set(ref headingPause, value); }
    public string Mode { get => mode; set => Set(ref mode, value); }
    public string Transition { get => transition; set => Set(ref transition, value); }
    public string? VoiceId { get => voiceId; set => Set(ref voiceId, value); }
    public bool AppLock { get => appLock; set => Set(ref appLock, value); }
    public void Normalize()
    {
        static double Range(double n, double min, double max, double fallback) => double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        FontSize = Range(FontSize, 14, 32, 20); Speed = Range(Speed, .5, 2.5, 1); Volume = Range(Volume, 0, 100, 80); Pitch = Range(Pitch, -25, 25, 0);
        SentencePause = Range(SentencePause, 0, 5, .25); ParagraphPause = Range(ParagraphPause, 0, 10, .65); HeadingPause = Range(HeadingPause, 0, 15, 1);
        if (Mode is not ("Natural" or "Rápida" or "Estudos" or "Personalizada")) Mode = "Natural";
        if (Transition is not ("Imediata" or "Ao final da frase" or "Ao final do parágrafo")) Transition = "Ao final da frase";
    }
}
