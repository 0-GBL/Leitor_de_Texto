using System.Diagnostics;
using System.IO;
using Leitor.Core;
using NAudio;
using NAudio.Wave;

namespace Leitor.Services;

public sealed class Playback : IDisposable
{
    private readonly ISpeechEngine engine;
    private CancellationTokenSource? session;
    private Task running = Task.CompletedTask;
    private readonly SemaphoreSlim controls = new(1, 1);
    private WaveOutEvent? output;
    private volatile bool paused;
    private volatile SpeechOptions desired = new(null, 1, 0, .25, .65, 1);
    private SpeechOptions active = new(null, 1, 0, .25, .65, 1);
    private string transition = "Ao final da frase";
    private IReadOnlyList<Segment> segments = [];
    private double[] durations = [];
    private readonly Dictionary<int, double> known = [];
    private readonly HashSet<int> generated = [];
    private double volume = .8, heldOffset, audioStart, audioDuration, silenceDuration, pauseBase, timelineAdjustment;
    private bool playingPause, deviceStarted;
    private readonly Stopwatch pauseClock = new();
    public int Index { get; private set; }
    public int AudibleIndex { get; private set; } = -1;
    public bool Playing { get; private set; }
    public bool Paused => paused;
    public double Offset
    {
        get
        {
            if (playingPause) return audioDuration + Math.Min(silenceDuration, pauseBase + pauseClock.Elapsed.TotalSeconds);
            var device = output;
            if (deviceStarted && device != null)
            {
                // Hardware cursor counts audible bytes, unlike WaveFileReader's prebuffer cursor.
                try { return Math.Min(audioDuration, audioStart + device.GetPosition() / (double)device.OutputWaveFormat.AverageBytesPerSecond); }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
            }
            return heldOffset;
        }
    }
    public double Position => Math.Max(0, durations.Take(Index).Sum() + Offset + timelineAdjustment);
    public double Duration => Math.Max(Position, durations.Sum() + timelineAdjustment);
    public double Buffered => durations.Select((d, i) => generated.Contains(i) ? d : 0).Sum();
    public Dictionary<int, double> KnownDurations => new(known);
    public event Action? Changed;
    public event Action<string>? Error;
    public Playback(ISpeechEngine engine) { this.engine = engine; }
    public void SetOptions(SpeechOptions options, string transition)
    {
        bool changed = desired != options;
        desired = options; this.transition = transition;
        if (!Playing) active = options;
        if (Playing && changed && transition == "Imediata") _ = RestartAsync(Index, Offset);
    }
    public void SetVolume(double value) { volume = Math.Clamp(value / 100, 0, 1); if (output != null) output.Volume = (float)volume; }
    public async Task LoadAsync(IReadOnlyList<Segment> value, IReadOnlyDictionary<int, double>? savedDurations = null)
    {
        await controls.WaitAsync();
        try
        {
            await StopCoreAsync(); segments = value; known.Clear(); generated.Clear();
            durations = value.Select(x =>
            {
                if (savedDurations?.TryGetValue(x.Start, out double d) == true && double.IsFinite(d) && d > 0)
                { known[x.Start] = d; return d; }
                return TextProcessor.Estimate(x, desired);
            }).ToArray();
            Index = 0; heldOffset = 0; timelineAdjustment = 0; AudibleIndex = -1; Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public Task StartAsync(int index = 0, double offset = 0) => RestartAsync(index, offset);
    public async Task RestoreAsync(int index, double offset, double? elapsed)
    {
        await controls.WaitAsync();
        try
        {
            await StopCoreAsync();
            Index = segments.Count == 0 ? 0 : Math.Clamp(index, 0, segments.Count - 1);
            heldOffset = double.IsFinite(offset) ? Math.Max(0, offset) : 0;
            timelineAdjustment = elapsed is double e && double.IsFinite(e) ? e - durations.Take(Index).Sum() - heldOffset : 0;
            Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public async Task RestartAsync(int index, double offset = 0)
    {
        await controls.WaitAsync();
        try
        {
            await StopCoreAsync();
            if (segments.Count == 0) return;
            Index = Math.Clamp(index, 0, segments.Count - 1); heldOffset = Math.Max(0, offset);
            active = desired; paused = false; Playing = true; AudibleIndex = -1;
            session = new(); running = RunAsync(Index, heldOffset, session.Token); Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public async Task RestartFromBeginningAsync()
    {
        await StopAsync(); timelineAdjustment = 0;
        await RestartAsync(0);
    }
    public void TogglePause()
    {
        if (!Playing) return;
        paused = !paused;
        if (paused) { output?.Pause(); pauseClock.Stop(); }
        else { if (deviceStarted) output?.Play(); if (playingPause) pauseClock.Start(); }
        Changed?.Invoke();
    }
    public async Task StopAsync()
    {
        await controls.WaitAsync();
        try { await StopCoreAsync(); } finally { controls.Release(); }
    }
    private async Task StopCoreAsync()
    {
        if (Playing)
        {
            if (deviceStarted) output?.Pause(); pauseClock.Stop();
            heldOffset = Offset;
        }
        session?.Cancel(); output?.Stop();
        try { await running; } catch (OperationCanceledException) { }
        session?.Dispose(); session = null; Playing = false; paused = false;
    }
    private SpeechOptions OptionsFor(int index, SpeechOptions previous)
    {
        var next = desired;
        if (transition == "Ao final do parágrafo" && index > 0 && segments[index - 1].Paragraph == segments[index].Paragraph)
            next = next with { Voice = previous.Voice };
        return next;
    }
    private async Task RunAsync(int start, double initialOffset, CancellationToken token)
    {
        Task<byte[]>? pending = null;
        SpeechOptions? pendingOptions = null;
        try
        {
            for (int i = start; i < segments.Count; i++)
            {
                token.ThrowIfCancellationRequested(); active = OptionsFor(i, active);
                byte[] audio;
                if (pending != null)
                {
                    audio = await pending;
                    if (pendingOptions != active) audio = await engine.GenerateAsync(segments[i].Text, active, token);
                }
                else audio = await engine.GenerateAsync(segments[i].Text, active, token);
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(audio);
                using var wave = new WaveFileReader(stream);
                Index = i; heldOffset = i == start ? initialOffset : 0;
                audioDuration = wave.TotalTime.TotalSeconds; silenceDuration = TextProcessor.Pause(segments[i], active);
                durations[i] = audioDuration + silenceDuration; known[segments[i].Start] = durations[i]; generated.Add(i);
                audioStart = Math.Min(heldOffset, audioDuration);
                wave.CurrentTime = TimeSpan.FromSeconds(audioStart);
                if (i + 1 < segments.Count)
                {
                    pendingOptions = OptionsFor(i + 1, active);
                    pending = engine.GenerateAsync(segments[i + 1].Text, pendingOptions, token);
                }
                else pending = null;
                using var device = new WaveOutEvent { DesiredLatency = 80 }; output = device;
                device.Init(wave); device.Volume = (float)volume;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                device.PlaybackStopped += (_, e) => { if (e.Exception != null) done.TrySetException(e.Exception); else done.TrySetResult(); };
                while (paused) await Task.Delay(20, token);
                device.Play(); deviceStarted = true; AudibleIndex = i; Changed?.Invoke();
                await done.Task.WaitAsync(token); token.ThrowIfCancellationRequested();
                deviceStarted = false; heldOffset = audioDuration;
                playingPause = true; pauseBase = Math.Max(0, (i == start ? initialOffset : 0) - audioDuration);
                pauseClock.Restart(); if (paused) pauseClock.Stop();
                while (pauseBase + pauseClock.Elapsed.TotalSeconds < silenceDuration) await Task.Delay(15, token);
                pauseClock.Stop(); heldOffset = audioDuration + silenceDuration; playingPause = false;
                output = null; AudibleIndex = -1; Changed?.Invoke();
            }
            Index = segments.Count; heldOffset = 0; AudibleIndex = -1; Changed?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Error?.Invoke(e.Message); }
        finally
        {
            try { if (pending != null) { session?.Cancel(); try { await pending; } catch (Exception) { } } }
            finally { output = null; deviceStarted = false; pauseClock.Stop(); playingPause = false; Playing = false; paused = false; Changed?.Invoke(); }
        }
    }
    public async Task SeekAsync(double seconds)
    {
        double target = Math.Clamp(seconds - timelineAdjustment, 0, Math.Max(0, durations.Sum() - .01)); int index = 0;
        while (index < durations.Length - 1 && target >= durations[index]) target -= durations[index++];
        await RestartAsync(index, target);
    }
    public async Task NavigateAsync(bool next, bool topics)
    {
        if (segments.Count == 0) return;
        var current = segments[Math.Clamp(Index, 0, segments.Count - 1)];
        var candidates = segments.Where(s => topics ? s.Heading : !s.Heading && (s.Index == 0 || segments[s.Index - 1].Paragraph != s.Paragraph));
        var target = next
            ? candidates.FirstOrDefault(s => topics ? s.Index > current.Index : s.Paragraph > current.Paragraph)
            : candidates.LastOrDefault(s => topics ? s.Index < current.Index : s.Paragraph < current.Paragraph);
        if (target != null) await RestartAsync(target.Index);
    }
    public void Dispose() { session?.Cancel(); output?.Stop(); }
}
