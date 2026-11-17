using System.Diagnostics;
using System.IO;
using Leitor.Core;
using NAudio;
using NAudio.Wave;

namespace Leitor.Services;

public readonly record struct PlaybackLocation(int Index, double Offset, double Position, bool Estimated, double Fraction);

public sealed class Playback : IDisposable
{
    private readonly ISpeechEngine engine;
    private readonly SemaphoreSlim controls = new(1, 1);
    private readonly object timingGate = new();
    private CancellationTokenSource? session;
    private Task running = Task.CompletedTask;
    private long transportVersion;
    private bool requestedPlaying, requestedPaused, disposed;
    private WaveOutEvent? output;
    private volatile bool paused;
    private volatile SpeechOptions desired = new(null, 1, 0, .25, .65, 1);
    private SpeechOptions active = new(null, 1, 0, .25, .65, 1);
    private string transition = "Ao final da frase";
    private IReadOnlyList<Segment> segments = [];
    private double[] durations = [];
    private readonly PlaybackTimeline timeline = new();
    private readonly Dictionary<int, double> known = [];
    private readonly HashSet<int> generated = [];
    private double bufferedSeconds, volume = .8, heldOffset, audioStart, audioDuration, silenceDuration, pauseBase, timelineAdjustment;
    private bool playingPause, deviceStarted;
    private readonly Stopwatch pauseClock = new();
    public int Index { get; private set; }
    public int AudibleIndex { get; private set; } = -1;
    public bool Playing { get; private set; }
    public bool Preparing { get; private set; }
    public bool Paused => paused;
    public bool HasActualTotal { get { lock (timingGate) return segments.Count > 0 && known.Count == segments.Count; } }
    public double Offset
    {
        get
        {
            if (playingPause) return audioDuration + Math.Min(silenceDuration, pauseBase + pauseClock.Elapsed.TotalSeconds);
            var device = output;
            if (deviceStarted && device != null)
            {
                try { return Math.Min(audioDuration, audioStart + device.GetPosition() / (double)device.OutputWaveFormat.AverageBytesPerSecond); }
                catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
            }
            return heldOffset;
        }
    }
    public double Position { get { lock (timingGate) return Math.Max(0, timeline.Before(Index) + Offset + timelineAdjustment); } }
    public double Duration { get { lock (timingGate) return Math.Max(Position, timeline.Total + timelineAdjustment); } }
    public double Buffered { get { lock (timingGate) return bufferedSeconds; } }
    public Dictionary<int, double> KnownDurations { get { lock (timingGate) return new(known); } }
    public event Action? Changed;
    public event Action<string>? Error;
    public Playback(ISpeechEngine engine) { this.engine = engine; }
    public void SetOptions(SpeechOptions options, string transition)
    {
        bool changed = desired != options;
        bool audioChanged = desired.Voice?.Id != options.Voice?.Id || desired.Speed != options.Speed || desired.Pitch != options.Pitch;
        double position = Position, offset = Offset, fraction = 0;
        lock (timingGate)
        {
            if (audioChanged && durations.Length > 0)
            {
                if (Index < durations.Length) fraction = offset / durations[Index];
                int from = Math.Clamp(Index + (requestedPlaying && transition != "Imediata" ? 1 : 0), 0, segments.Count);
                for (int i = from; i < segments.Count; i++) { known.Remove(segments[i].Start); generated.Remove(i); durations[i] = TextProcessor.Estimate(segments[i], options); }
                timeline.Reset(durations); bufferedSeconds = generated.Sum(i => durations[i]);
                timelineAdjustment = position - timeline.Before(Index) - offset;
            }
        }
        desired = options; this.transition = transition;
        if (!requestedPlaying) active = options;
        if (requestedPlaying && changed && transition == "Imediata")
        {
            var location = new PlaybackLocation(Index, offset, position, audioChanged, fraction);
            _ = MoveAsync(location, true, requestedPaused, true);
        }
    }
    public void SetVolume(double value)
    {
        volume = Math.Clamp(value / 100, 0, 1);
        try { if (output != null) output.Volume = (float)volume; }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
    }
    private long Request()
    {
        long version = Interlocked.Increment(ref transportVersion);
        try { session?.Cancel(); } catch (ObjectDisposedException) { }
        return version;
    }
    public async Task LoadAsync(IReadOnlyList<Segment> value, IReadOnlyDictionary<int, double>? savedDurations = null)
    {
        requestedPlaying = requestedPaused = false; long version = Request(); await controls.WaitAsync();
        try
        {
            if (version != transportVersion || disposed) return;
            await StopCoreAsync();
            lock (timingGate)
            {
                segments = value; known.Clear(); generated.Clear();
                durations = value.Select(x =>
                {
                    if (savedDurations?.TryGetValue(x.Start, out double d) == true && double.IsFinite(d) && d > 0) { known[x.Start] = d; return d; }
                    return TextProcessor.Estimate(x, desired);
                }).ToArray();
                timeline.Reset(durations); bufferedSeconds = 0;
                Index = 0; heldOffset = 0; timelineAdjustment = 0; AudibleIndex = -1;
            }
            Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public Task StartAsync(int index = 0, double offset = 0) => RestartAsync(index, offset);
    public async Task RestoreAsync(int index, double offset, double? elapsed)
    {
        requestedPlaying = requestedPaused = false; long version = Request(); await controls.WaitAsync();
        try
        {
            if (version != transportVersion || disposed) return;
            await StopCoreAsync();
            lock (timingGate)
            {
                Index = segments.Count == 0 ? 0 : Math.Clamp(index, 0, segments.Count - 1);
                heldOffset = double.IsFinite(offset) ? Math.Max(0, offset) : 0;
                timelineAdjustment = elapsed is double e && double.IsFinite(e) ? e - timeline.Before(Index) - heldOffset : 0;
            }
            Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public Task RestartAsync(int index, double offset = 0)
    {
        PlaybackLocation location;
        lock (timingGate)
        {
            index = Math.Clamp(index, 0, Math.Max(0, segments.Count - 1));
            double position = timeline.Before(index) + Math.Max(0, offset) + timelineAdjustment;
            bool estimated = segments.Count > 0 && !known.ContainsKey(segments[index].Start);
            location = new(index, offset, position, estimated, durations.Length == 0 ? 0 : offset / durations[index]);
        }
        return MoveAsync(location, true, false, false);
    }
    public Task RestartFromBeginningAsync() => MoveAsync(new(0, 0, 0, false, 0), true, false, false);
    public Task JumpAsync(int index) => MoveAsync(new(index, 0, double.NaN, false, 0), true, requestedPaused, true);
    private async Task MoveAsync(PlaybackLocation location, bool play, bool pause, bool coalesce)
    {
        requestedPlaying = play; requestedPaused = pause;
        long version = Request();
        // Rapid transport input replaces older requests before they start synthesis.
        if (coalesce) await Task.Delay(40);
        if (version != transportVersion || disposed) return;
        await controls.WaitAsync();
        try
        {
            if (version != transportVersion || disposed) return;
            await StopCoreAsync();
            if (segments.Count == 0) return;
            Index = Math.Clamp(location.Index, 0, segments.Count - 1);
            heldOffset = double.IsFinite(location.Offset) ? Math.Max(0, location.Offset) : 0;
            lock (timingGate)
            {
                if (double.IsFinite(location.Position)) timelineAdjustment = location.Position - timeline.Before(Index) - heldOffset;
            }
            active = desired; paused = requestedPaused; Playing = play; Preparing = play; AudibleIndex = -1;
            if (play) { session = new(); running = RunAsync(Index, location, version, session.Token); }
            Changed?.Invoke();
        }
        finally { controls.Release(); }
    }
    public void TogglePause()
    {
        if (!requestedPlaying) return;
        requestedPaused = !requestedPaused; paused = requestedPaused;
        try { if (paused) output?.Pause(); else if (deviceStarted) output?.Play(); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { Error?.Invoke("A saída de áudio mudou. Interrompa e continue a leitura para reconectar o dispositivo."); }
        if (paused) pauseClock.Stop(); else if (playingPause) pauseClock.Start();
        Changed?.Invoke();
    }
    public async Task StopAsync()
    {
        requestedPlaying = requestedPaused = false; long version = Request(); await controls.WaitAsync();
        try { if (version == transportVersion) { await StopCoreAsync(); Changed?.Invoke(); } }
        finally { controls.Release(); }
    }
    private async Task StopCoreAsync()
    {
        try { if (deviceStarted) output?.Pause(); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
        pauseClock.Stop(); heldOffset = Offset; deviceStarted = false;
        try { session?.Cancel(); output?.Stop(); }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
        try { await running; } catch (OperationCanceledException) { }
        session?.Dispose(); session = null; Playing = Preparing = false; paused = false;
    }
    private SpeechOptions OptionsFor(int index, SpeechOptions previous)
    {
        var next = desired;
        if (transition == "Ao final do parágrafo" && index > 0 && segments[index - 1].Paragraph == segments[index].Paragraph) next = next with { Voice = previous.Voice };
        return next;
    }
    private static void Observe(Task task) => _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private async Task<byte[]> GenerateAsync(int index, SpeechOptions options, CancellationToken token)
    {
        Task<byte[]> task = engine.GenerateAsync(segments[index].Text, options, token); Observe(task);
        return await task.WaitAsync(token);
    }
    private async Task RunAsync(int start, PlaybackLocation location, long version, CancellationToken token)
    {
        Task<byte[]>? pending = null; SpeechOptions? pendingOptions = null;
        try
        {
            for (int i = start; i < segments.Count; i++)
            {
                token.ThrowIfCancellationRequested(); active = OptionsFor(i, active); Preparing = true; Changed?.Invoke();
                byte[] audio;
                if (pending != null)
                {
                    audio = await pending.WaitAsync(token); pending = null;
                    if (pendingOptions != active) audio = await GenerateAsync(i, active, token);
                }
                else audio = await GenerateAsync(i, active, token);
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(audio); using var wave = new WaveFileReader(stream);
                Index = i; heldOffset = i == start ? Math.Max(0, location.Offset) : 0;
                audioDuration = wave.TotalTime.TotalSeconds; silenceDuration = TextProcessor.Pause(segments[i], active);
                lock (timingGate)
                {
                    double previousDuration = durations[i]; durations[i] = audioDuration + silenceDuration;
                    timeline.Add(i, durations[i] - previousDuration);
                    bufferedSeconds += generated.Add(i) ? durations[i] : durations[i] - previousDuration;
                    known[segments[i].Start] = durations[i];
                    if (i == start && location.Estimated) heldOffset = Math.Clamp(location.Fraction, 0, 1) * durations[i];
                    heldOffset = Math.Clamp(heldOffset, 0, durations[i]);
                    if (i == start && double.IsFinite(location.Position)) timelineAdjustment = location.Position - timeline.Before(i) - heldOffset;
                }
                audioStart = Math.Min(heldOffset, audioDuration); wave.CurrentTime = TimeSpan.FromSeconds(audioStart);
                double destinationOffset = heldOffset;
                using var device = new WaveOutEvent { DesiredLatency = 80 }; output = device;
                try
                {
                    device.Init(wave); device.Volume = (float)volume;
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    device.PlaybackStopped += (_, e) => { if (e.Exception != null) done.TrySetException(e.Exception); else done.TrySetResult(); };
                    Preparing = false;
                    if (paused) { AudibleIndex = i; Changed?.Invoke(); }
                    while (paused) await Task.Delay(20, token);
                    token.ThrowIfCancellationRequested();
                    bool hasAudio = audioStart < audioDuration;
                    if (hasAudio) { device.Play(); deviceStarted = true; }
                    AudibleIndex = i; Changed?.Invoke();
                    // A paused destination is prepared alone. The next sentence is prefetched only after playback starts.
                    if (i + 1 < segments.Count) { pendingOptions = OptionsFor(i + 1, active); pending = GenerateAsync(i + 1, pendingOptions, token); Observe(pending); }
                    if (hasAudio) await done.Task.WaitAsync(token); token.ThrowIfCancellationRequested();
                    deviceStarted = false; heldOffset = audioDuration;
                    playingPause = true; pauseBase = Math.Max(0, destinationOffset - audioDuration);
                    pauseClock.Restart(); if (paused) pauseClock.Stop();
                    while (pauseBase + pauseClock.Elapsed.TotalSeconds < silenceDuration) await Task.Delay(15, token);
                    pauseClock.Stop(); heldOffset = audioDuration + silenceDuration; playingPause = false;
                }
                finally { heldOffset = Offset; deviceStarted = false; playingPause = false; output = null; }
            }
            Index = segments.Count; heldOffset = 0; AudibleIndex = -1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { Error?.Invoke("Não foi possível continuar o áudio. Verifique a voz e o dispositivo de saída e tente continuar a leitura."); }
        finally
        {
            bool interrupted = token.IsCancellationRequested;
            if (pending != null) Observe(pending);
            try { session?.Cancel(); } catch (ObjectDisposedException) { }
            output = null; deviceStarted = false; pauseClock.Stop(); playingPause = false; Playing = Preparing = false; paused = false;
            if (!interrupted && version == transportVersion) requestedPlaying = requestedPaused = false;
            Changed?.Invoke();
        }
    }
    public PlaybackLocation Locate(double seconds)
    {
        lock (timingGate)
        {
            double position = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, Math.Max(0, timeline.Total + timelineAdjustment - .001));
            double target = Math.Clamp(position - timelineAdjustment, 0, Math.Max(0, timeline.Total - .001));
            int index = timeline.Locate(target); double offset = target - timeline.Before(index);
            bool estimated = segments.Count > 0 && !known.ContainsKey(segments[index].Start);
            return new(index, offset, position, estimated, durations.Length == 0 ? 0 : offset / durations[index]);
        }
    }
    public Task SeekAsync(double seconds) => SeekAsync(Locate(seconds));
    public Task SeekAsync(PlaybackLocation location) => MoveAsync(location, requestedPlaying, requestedPaused, true);
    public async Task NavigateAsync(bool next, bool topics)
    {
        if (segments.Count == 0) return;
        var current = segments[Math.Clamp(Index, 0, segments.Count - 1)];
        var candidates = segments.Where(s => topics ? s.Heading : !s.Heading && (s.Index == 0 || segments[s.Index - 1].Paragraph != s.Paragraph));
        var target = next ? candidates.FirstOrDefault(s => topics ? s.Index > current.Index : s.Paragraph > current.Paragraph) : candidates.LastOrDefault(s => topics ? s.Index < current.Index : s.Paragraph < current.Paragraph);
        if (target != null) await JumpAsync(target.Index);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; requestedPlaying = requestedPaused = false; Request();
        try { output?.Stop(); } catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or MmException) { }
    }
}
