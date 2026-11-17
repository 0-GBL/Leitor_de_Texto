using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Leitor.Presentation;

public sealed class SeekPreviewEventArgs(double value, bool interacting) : EventArgs
{ public double Value { get; } = value; public bool Interacting { get; } = interacting; }
public sealed class SeekCommitEventArgs(double value, long interaction) : EventArgs
{ public double Value { get; } = value; public long Interaction { get; } = interaction; }

// Owns pointer/keyboard interaction; the player is contacted only on commit.
public sealed class SeekSlider : Slider
{
    private bool holding, dragging, releasing, committed;
    private double preview, grabOffset;
    private long interaction;
    public bool IsInteracting => holding;
    public event EventHandler? InteractionStarted;
    public event EventHandler? InteractionEnded;
    public event EventHandler<SeekPreviewEventArgs>? PreviewPositionChanged;
    public event EventHandler<SeekCommitEventArgs>? SeekCommitted;
    static SeekSlider() => ValueProperty.OverrideMetadata(typeof(SeekSlider), new FrameworkPropertyMetadata(0d, null, (d, value) => ((SeekSlider)d).holding ? ((SeekSlider)d).preview : value));
    public SeekSlider() { SetResourceReference(StyleProperty, typeof(Slider)); IsSnapToTickEnabled = false; }
    private void Begin()
    {
        preview = Value; holding = true; committed = false; interaction++; InteractionStarted?.Invoke(this, EventArgs.Empty);
    }
    private void Preview(double value)
    {
        preview = Math.Clamp(value, Minimum, Maximum); CoerceValue(ValueProperty);
        PreviewPositionChanged?.Invoke(this, new(preview, true));
    }
    private double PointerValue(double x, double offset = 0)
    {
        double thumb = (Template.FindName("PART_Track", this) as Track)?.Thumb?.ActualWidth ?? 16;
        double fraction = Math.Clamp((x - thumb / 2 - offset) / Math.Max(1, ActualWidth - thumb), 0, 1);
        return Minimum + (Maximum - Minimum) * (IsDirectionReversed ? 1 - fraction : fraction);
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true; Focus();
        var track = Template.FindName("PART_Track", this) as Track;
        BeginPointer(e.GetPosition(this).X, track?.Thumb?.IsMouseOver == true); CaptureMouse();
    }
    private void BeginPointer(double x, bool onThumb)
    {
        Begin(); dragging = true;
        grabOffset = onThumb ? x - (8 + (ActualWidth - 16) * (Value - Minimum) / Math.Max(.001, Maximum - Minimum)) : 0;
        Preview(PointerValue(x, grabOffset));
    }
    private void MovePointer(double x) => Preview(PointerValue(x, grabOffset));
    private void EndPointer(double x) { MovePointer(x); dragging = false; Commit(); }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (dragging) { e.Handled = true; MovePointer(e.GetPosition(this).X); }
        else if (!holding) PreviewPositionChanged?.Invoke(this, new(PointerValue(e.GetPosition(this).X), false));
        base.OnPreviewMouseMove(e);
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!dragging) return;
        e.Handled = true; EndPointer(e.GetPosition(this).X);
        releasing = true; ReleaseMouseCapture(); releasing = false;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    { base.OnLostMouseCapture(e); if (dragging && !releasing) { dragging = false; CompleteSeek(interaction); } }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && holding) { e.Handled = true; dragging = false; CompleteSeek(interaction); ReleaseMouseCapture(); return; }
        if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true; if (!holding || committed) Begin();
        Preview(e.Key switch { Key.Home => Minimum, Key.End => Maximum, Key.Left => preview - SmallChange, Key.Right => preview + SmallChange, Key.PageDown => preview - LargeChange, _ => preview + LargeChange });
    }
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    { if (holding && !dragging && e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) { e.Handled = true; Commit(); } else base.OnPreviewKeyUp(e); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    { if (holding && !dragging) Commit(); base.OnLostKeyboardFocus(e); }
    private void Commit()
    { if (committed) return; committed = true; if (SeekCommitted == null) CompleteSeek(interaction); else SeekCommitted.Invoke(this, new(preview, interaction)); }
    public void CompleteSeek(long id)
    { if (id != interaction) return; holding = false; CoerceValue(ValueProperty); InteractionEnded?.Invoke(this, EventArgs.Empty); }
    public void CancelInteraction()
    { dragging = false; CompleteSeek(interaction); ReleaseMouseCapture(); }
    protected override AutomationPeer OnCreateAutomationPeer() => new SeekPeer(this);
    private sealed class SeekPeer(SeekSlider slider) : SliderAutomationPeer(slider), IRangeValueProvider
    {
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        bool IRangeValueProvider.IsReadOnly => !slider.IsEnabled;
        double IRangeValueProvider.LargeChange => slider.LargeChange;
        double IRangeValueProvider.SmallChange => slider.SmallChange;
        double IRangeValueProvider.Maximum => slider.Maximum;
        double IRangeValueProvider.Minimum => slider.Minimum;
        double IRangeValueProvider.Value => slider.Value;
        void IRangeValueProvider.SetValue(double value)
        {
            if (!slider.IsEnabled) throw new ElementNotEnabledException();
            if (!double.IsFinite(value) || value < slider.Minimum || value > slider.Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            slider.Begin(); slider.Preview(value); slider.Commit();
        }
    }
}
