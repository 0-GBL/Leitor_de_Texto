using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Leitor.ViewModels;

namespace Leitor.Presentation;

public partial class LibraryPanel : UserControl
{
    private const string DragFormat = "Leitor.LibraryItem";
    private Point dragStart;
    private DragReference? dragged;
    private double availableWidth = 800;
    private sealed record DragReference(string Id, bool Folder);

    public LibraryPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AdaptWidth(availableWidth);
    }
    private MainViewModel? Model => DataContext as MainViewModel;
    public TextBox SearchInput => SearchBox;

    internal void AdaptWidth(double width)
    {
        availableWidth = width;
        bool collapsed = Model?.Settings.LibrarySidebarCollapsed == true;
        FolderSidebar.Visibility = SidebarSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarGap.Width = new(collapsed ? 0 : 12);
        string label = collapsed ? "Expandir painel de pastas" : "Recolher painel de pastas";
        SidebarToggle.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(SidebarToggle, label);
        double maximum = Math.Max(200, Math.Min(520, width * .36));
        LibraryColumn.MinWidth = collapsed ? 0 : 200;
        LibraryColumn.MaxWidth = maximum;
        LibraryColumn.Width = new(collapsed ? 0 : Math.Clamp(Model?.Settings.LibraryWidth ?? 280, 200, maximum));
    }
    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        if (Model is not {} vm) return;
        vm.Settings.LibrarySidebarCollapsed = !vm.Settings.LibrarySidebarCollapsed;
        AdaptWidth(availableWidth);
        vm.SavePreferences();
    }
    private void ToggleOptions(object sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = !OptionsPopup.IsOpen;
        if (OptionsPopup.IsOpen && OptionsPopup.Child is UIElement content)
            content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
    private void OptionsKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        OptionsPopup.IsOpen = false;
        OptionsButton.Focus();
        e.Handled = true;
    }
    private void LibraryResized(object sender, DragCompletedEventArgs e)
    {
        if (Model is not {} vm) return;
        vm.Settings.LibraryWidth = LibraryColumn.ActualWidth;
        vm.SavePreferences();
    }

    private void OpenEntry(object sender, MouseButtonEventArgs e)
    {
        if (Row(sender, e.OriginalSource) is not { DataContext: LibraryEntry } row || Model is not {} vm) return;
        row.IsSelected = true;
        vm.ActivateEntryCommand.Execute(null);
        e.Handled = true;
    }

    private void EntryKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || Model is not {} vm) return;
        vm.ActivateEntryCommand.Execute(null);
        e.Handled = true;
    }

    private void NavigateCrumb(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryCrumb crumb }) Model?.NavigateFolder(crumb.Id);
    }

    private void ExpandFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow folder }) Model?.ToggleFolder(folder.Id);
        e.Handled = true;
    }

    private void FolderKey(object sender, KeyEventArgs e)
    {
        if (Model is not { SelectedFolder: {} folder } vm || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Enter: vm.NavigateFolder(folder.Id); break;
            case Key.Right: vm.ToggleFolder(folder.Id, true); break;
            case Key.Left:
                if (folder.Expanded) vm.ToggleFolder(folder.Id, false);
                else vm.NavigateFolder(folder.ParentId);
                break;
            default: return;
        }
        e.Handled = true;
    }

    private static ListBoxItem? Row(object sender, object source) =>
        source is DependencyObject element ? ItemsControl.ContainerFromElement((ItemsControl)sender, element) as ListBoxItem : null;

    private void SelectContext(object sender, MouseButtonEventArgs e)
    {
        var item = Row(sender, e.OriginalSource);
        if (item != null) { item.IsSelected = true; item.Focus(); }
        else if (sender == LibraryList) LibraryList.SelectedIndex = -1;
    }
    private void TreeFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Model is not {} vm) return;
        vm.SelectedEntry = null;
        vm.FolderContext = true;
    }
    private void EntryFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Model is { SelectedEntry: {} entry } vm) vm.FolderContext = entry.Folder != null;
    }

    private void BuildMenu(ContextMenu menu, bool folder)
    {
        if (Model is not {} vm) return;
        vm.FolderContext = folder;
        menu.Items.Clear();
        void Add(string label, ICommand command) => menu.Items.Add(new MenuItem { Header = label, Command = command });

        if (folder)
        {
            menu.Items.Add(new MenuItem { Header = "Abrir pasta", Command = vm.OpenFolderCommand });
            Add("Nova subpasta…", vm.NewSubfolderCommand);
        }
        else Add("Abrir", vm.OpenCommand);
        Add("Renomear · F2", vm.RenameItemCommand);
        Add("Mover para…", vm.MoveItemCommand);
        Add("Copiar para…", vm.CopyItemCommand);
        if (folder)
        {
            var colors = new MenuItem { Header = "Cor da pasta" };
            foreach (var (name, color) in FolderColorConverter.Choices)
            {
                var choice = new MenuItem { Header = name };
                choice.Click += (_, _) => vm.ColorSelectedFolder(color);
                colors.Items.Add(choice);
            }
            menu.Items.Add(colors);
        }
        Add(folder ? "Excluir pasta…" : "Excluir documento…", folder ? vm.DeleteFolderCommand : vm.DeleteCommand);
    }

    private void EntryMenuOpened(object sender, RoutedEventArgs e)
    {
        if (Model?.SelectedEntry is not {} entry) { ((ContextMenu)sender).IsOpen = false; return; }
        BuildMenu((ContextMenu)sender, entry.Folder != null);
    }

    private void TreeMenuOpened(object sender, RoutedEventArgs e)
    {
        if (Model is not { SelectedFolder: not null } vm) { ((ContextMenu)sender).IsOpen = false; return; }
        vm.SelectedEntry = null;
        BuildMenu((ContextMenu)sender, true);
    }

    private static DragReference? DragItemOf(ListBoxItem? row) => row?.DataContext switch
    {
        LibraryEntry entry => new(entry.Id, entry.Folder != null),
        FolderRow folder => new(folder.Id, true),
        _ => null
    };

    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        dragged = e.OriginalSource is DependencyObject source && !InsideButton(source)
            ? DragItemOf(Row(sender, source)) : null;
    }

    private static bool InsideButton(DependencyObject source)
    {
        for (var element = source; element is Visual; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase) return true;
            if (element is ListBoxItem) return false;
        }
        return false;
    }

    private void DragItem(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragged is not {} item) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X-dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y-dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragged = null;
        DragDrop.DoDragDrop(this, new DataObject(DragFormat, item), DragDropEffects.Move | DragDropEffects.Copy);
    }

    private static bool CopyRequested(DragEventArgs e) => (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
    private static string? FolderAt(object sender, object source) => Row(sender, source)?.DataContext switch
    {
        LibraryEntry { Folder: {} folder } => folder.Id,
        FolderRow folder => folder.Id,
        _ => null
    };

    private void DragOverFolder(object sender, DragEventArgs e)
    {
        bool destination = sender is Button || FolderAt(sender, e.OriginalSource) != null;
        e.Effects = destination && e.Data.GetDataPresent(DragFormat, false)
            ? CopyRequested(e) ? DragDropEffects.Copy : DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private async void DropFolder(object sender, DragEventArgs e)
    {
        if (FolderAt(sender, e.OriginalSource) is string destination) await TransferAsync(e, destination);
    }
    private async void DropCrumb(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryCrumb crumb }) await TransferAsync(e, crumb.Id);
    }
    private async Task TransferAsync(DragEventArgs e, string? destination)
    {
        if (e.Data.GetData(DragFormat, false) is not DragReference item || Model is not {} vm) return;
        e.Handled = true;
        try { await vm.MoveLibraryItemAsync(item.Id, item.Folder, destination, CopyRequested(e)); }
        catch (Exception error) { vm.Status = error.Message; }
    }
}

public sealed class FolderColorConverter : IValueConverter
{
    internal static (string Name, string? Color)[] Choices { get; } =
        [("Padrão", null), ("Âmbar", "#A77220"), ("Verde", "#3D865F"), ("Azul", "#477FB7"), ("Violeta", "#9272AA")];
    private static readonly Dictionary<string, Brush> BrushesByColor = Choices
        .Where(choice => choice.Color != null)
        .ToDictionary(choice => choice.Color!, choice =>
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Color!)!);
            brush.Freeze();
            return (Brush)brush;
        });

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string color && BrushesByColor.TryGetValue(color, out var brush) ? brush : DependencyProperty.UnsetValue;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class LibraryTileWidth : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width <= 0) return 208d;
        double usable = Math.Max(1, width - 28);
        return Math.Floor(usable / Math.Max(1, Math.Floor(usable / 205)));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
