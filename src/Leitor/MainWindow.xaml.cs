using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leitor.ViewModels;
using Leitor.Services;

namespace Leitor;
public partial class MainWindow : Window
{
    private MainViewModel? vm;
    private bool ready, changingMode, closing, allowClose;
    public MainWindow() : this(new MainViewModel()) { }
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); vm = model; DataContext = vm;
        vm.ThemeChanged += ApplyTheme;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.NarratedSegment) && vm.Settings.AutoScroll && vm.NarratedSegment != null) ReaderList.ScrollIntoView(vm.NarratedSegment); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) { if (vm == null) return; ApplyTheme(); await vm.InitializeAsync(); ready = true; }
    private void ApplyTheme()
    {
        if (vm == null) return;
        ThemeService.Apply(vm.Settings.Dark);
    }
    private void SettingChanged(object sender, RoutedEventArgs e) { if (ready && vm != null && !changingMode) { if (sender is Slider slider) slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource(); vm.ApplySettings(); } }
    private void SelectionSettingChanged(object sender, SelectionChangedEventArgs e)
    { if (ready) ((ComboBox)sender).GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource(); SettingChanged(sender, e); }
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    { if (!ready || changingMode || vm == null) return; changingMode = true; ((ComboBox)sender).GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource(); vm.SetMode(); changingMode = false; }
    private void CustomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!ready || changingMode || vm == null) return; changingMode = true; ((Slider)sender).GetBindingExpression(Slider.ValueProperty)?.UpdateSource(); vm.Settings.Mode = "Personalizada"; vm.ApplySettings(); changingMode = false; }
    private void OpenSettings(object sender, RoutedEventArgs e) { if (vm != null) new SettingsWindow(vm) { Owner = this }.ShowDialog(); }
    private void ManageVoices(object sender, RoutedEventArgs e)
    { if (vm == null) return; new VoiceManager { Owner = this }.ShowDialog(); vm.ReloadVoices(); }
    private void OpenDocument(object sender, MouseButtonEventArgs e) => vm?.OpenCommand.Execute(null);
    private void JumpToSegment(object sender, MouseButtonEventArgs e) => vm?.JumpCommand.Execute(null);
    private void ReaderKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { vm?.JumpCommand.Execute(null); e.Handled = true; } }
    private async void SeekReleased(object sender, MouseButtonEventArgs e) { if (vm != null) try { await vm.SeekPercentAsync(Timeline.Value); } catch (Exception ex) { vm.Status = ex.Message; } }
    private async void SeekKey(object sender, KeyEventArgs e) { if (e.Key is Key.Left or Key.Right or Key.Home or Key.End && vm != null) await vm.SeekPercentAsync(Timeline.Value); }
    protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); if (ready && vm is { Settings.Background: false } && vm.Player.Playing && !vm.Player.Paused) vm.Player.TogglePause(); }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose || vm == null) return; e.Cancel = true; if (closing) return; closing = true;
        try { await vm.ShutdownAsync(); allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
        catch (Exception ex) { vm.Status = "Falha ao salvar antes de fechar: " + ex.Message; closing = false; }
    }
}
