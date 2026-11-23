using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Leitor.ViewModels;
using Leitor.Services;
using Leitor.Presentation;
using Microsoft.Win32;

namespace Leitor;
public partial class MainWindow : Window
{
    private MainViewModel? vm;
    private bool ready, changingMode, closing, allowClose;
    public MainWindow() : this(new MainViewModel()) { }
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); vm = model; vm.EditorOwnsMetrics=true; DataContext = vm;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 16); Height = Math.Min(Height, SystemParameters.WorkArea.Height - 16);
        vm.ThemeChanged += ApplyTheme;
        SystemEvents.PowerModeChanged += PowerChanged;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.NarratedSegment) && vm.Settings.AutoScroll && vm.NarratedSegment != null) ReaderList.ScrollIntoView(vm.NarratedSegment); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) { if (vm == null) return; ApplyTheme(); await vm.InitializeAsync(); ready = true; }
    private void AdaptiveLayout(object sender, SizeChangedEventArgs e)
    {
        if (Sidebar == null) return;
        NarrationColumn.Width = new GridLength(ActualWidth < 1000 ? 228 : 272);
        Sidebar.AdaptWidth(ActualWidth - 44);
    }
    public TextBox SearchBox=>Sidebar.SearchInput;
    private void EditorTextChanged(object sender,TextChangedEventArgs e){if(vm!=null)vm.ApplyEditorStatistics(EditorBox.Text,e.Changes);}
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
    private void OpenHelp(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this, ShowActivated = IsActive }.ShowDialog();
    private async void ManageVoices(object sender, RoutedEventArgs e)
    { if (vm == null) return; try { await vm.PrepareVoiceManagementAsync(); new VoiceManager { Owner = this }.ShowDialog(); await vm.ReloadVoicesAsync(); } catch (Exception error) { vm.Status = "Não foi possível abrir os recursos de voz: " + error.Message; } }
    private void JumpToSegment(object sender, MouseButtonEventArgs e) => vm?.JumpCommand.Execute(null);
    private double seekDuration;
    private void SeekStarted(object? sender, EventArgs e)
    {
        seekDuration = vm?.SeekDuration ?? 0;
        Timeline.SmallChange = seekDuration > 0 ? 500 / seekDuration : 1;
        Timeline.LargeChange = seekDuration > 0 ? 3000 / seekDuration : 10;
    }
    private void SeekPreview(object? sender, SeekPreviewEventArgs e)
    {
        if (vm == null) return;
        string label = vm.PreviewSeek(e.Value, e.Interacting ? seekDuration : vm.SeekDuration);
        Timeline.ToolTip = label;
        if (e.Interacting) { SeekPreviewText.Text = "Prévia · " + label; SeekPreviewText.Visibility = Visibility.Visible; }
    }
    private void SeekEnded(object? sender, EventArgs e) => SeekPreviewText.Visibility = Visibility.Collapsed;
    private async void SeekReleased(object? sender, SeekCommitEventArgs e)
    {
        if (vm == null) return;
        try { await vm.SeekPercentAsync(e.Value, seekDuration); }
        catch (OperationCanceledException) { }
        catch (Exception) { vm.Status = "Não foi possível ir até esse trecho. Tente novamente após verificar a saída de áudio."; }
        finally { Timeline.CompleteSeek(e.Interaction); }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Suspend) _ = Dispatcher.BeginInvoke(new Action(async () => { if (vm != null) await vm.SuspendAsync(); })); }
    protected override void OnClosed(EventArgs e) { SystemEvents.PowerModeChanged -= PowerChanged; base.OnClosed(e); }
    protected override void OnDeactivated(EventArgs e) { base.OnDeactivated(e); if (ready && vm is { Settings.Background: false } && vm.Player.Playing && !vm.Player.Paused) vm.Player.TogglePause(); }
    public static bool IsEditing(DependencyObject? element)
    {
        for (var current = element; current != null; current = ContainerAncestor(current))
            if (current is TextBoxBase or PasswordBox || current is ComboBox { IsEditable: true }) return true;
        return false;
    }
    private static DependencyObject? ContainerAncestor(DependencyObject child) => child is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (vm == null || e.Handled || e.IsRepeat) return;
        var focus = Keyboard.FocusedElement as DependencyObject;
        var modifiers = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        ICommand? navigation = (key, modifiers) switch
        {
            (Key.L, ModifierKeys.Control) => vm.ShowLibraryCommand,
            (Key.F11, ModifierKeys.None) => vm.ToggleFocusCommand,
            (Key.Left, ModifierKeys.Alt) when vm.LibraryVisible => vm.NavigateBackCommand,
            (Key.Right, ModifierKeys.Alt) when vm.LibraryVisible => vm.NavigateForwardCommand,
            _ => null
        };
        if (navigation != null)
        {
            if (navigation.CanExecute(null)) navigation.Execute(null);
            e.Handled = true;
            return;
        }
        if(Sidebar.IsKeyboardFocusWithin&&!IsEditing(focus))
        {
            ICommand? organization=Shortcuts.ResolveLibrary(key,modifiers,false) switch
            {
                "new"=>vm.NewFolderCommand, "move"=>vm.MoveItemCommand, "copy"=>vm.CopyItemCommand,
                "rename"=>vm.RenameItemCommand, "delete"=>vm.FolderContext ? vm.DeleteFolderCommand : vm.DeleteCommand, _=>null
            };
            if(organization!=null){e.Handled=true;if(organization.CanExecute(null))organization.Execute(null);return;}
        }
        if (focus == Timeline && Timeline.IsInteracting && e.Key == Key.Escape) { Timeline.CancelInteraction(); e.Handled = true; return; }
        if (focus is ComboBox { IsDropDownOpen: true } && e.Key == Key.Escape) return;
        bool nativeSpace = focus is ButtonBase or Slider or ComboBox;
        var action = Shortcuts.Resolve(e.Key, Keyboard.Modifiers, IsEditing(focus), nativeSpace, ReaderList.IsKeyboardFocusWithin);
        if (action == null) return;
        if (action == ShortcutAction.Stop && !vm.Player.Playing && !vm.IsBuffering) return;
        e.Handled = true;
        ICommand? command = action switch
        {
            ShortcutAction.New => vm.NewCommand, ShortcutAction.Save => vm.SaveCommand, ShortcutAction.Play => vm.PlayCommand,
            ShortcutAction.Stop => vm.StopCommand, ShortcutAction.Restart => vm.RestartCommand, ShortcutAction.Back => vm.BackCommand,
            ShortcutAction.Forward => vm.ForwardCommand, ShortcutAction.PreviousParagraph => vm.PreviousParagraphCommand,
            ShortcutAction.NextParagraph => vm.NextParagraphCommand, ShortcutAction.PreviousHeading => vm.PreviousTopicCommand,
            ShortcutAction.NextHeading => vm.NextTopicCommand, ShortcutAction.Theme => vm.ToggleThemeCommand, ShortcutAction.Jump => vm.JumpCommand, _ => null
        };
        if (command != null) { if (command.CanExecute(null)) command.Execute(null); return; }
        switch (action)
        {
            case ShortcutAction.Paste: vm.ShowEditor(); UpdateLayout(); EditorBox.Focus(); EditorBox.Paste(); break;
            case ShortcutAction.Search: vm.ShowLibraryCommand.Execute(null); UpdateLayout(); SearchBox.Focus(); SearchBox.SelectAll(); break;
            case ShortcutAction.Settings: OpenSettings(this, new()); break;
            case ShortcutAction.Help: OpenHelp(this, new()); break;
        }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose || vm == null) return; e.Cancel = true; if (closing) return; closing = true; IsEnabled = false;
        try { await vm.ShutdownAsync(); allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
        catch (Exception) { vm.Status = "Não foi possível salvar antes de fechar. Verifique o espaço e a permissão da pasta; o texto permanece aberto para tentar novamente."; closing = false; IsEnabled = true; }
    }
}
