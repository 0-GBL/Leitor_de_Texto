using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leitor.Core;
using Leitor.ViewModels;

namespace Leitor.Presentation;
public partial class LibraryPanel : UserControl
{
    private Point dragStart;
    private sealed record DragReference(string Id,bool Folder);
    public LibraryPanel(){InitializeComponent();}
    private MainViewModel? Model=>DataContext as MainViewModel;
    public TextBox SearchInput=>SearchBox;
    private void LibraryFocus(object s,KeyboardFocusChangedEventArgs e){if(Model!=null)Model.FolderContext=s==FolderTree;}
    private void OpenDocument(object s,MouseButtonEventArgs e)=>Model?.OpenCommand.Execute(null);
    private void ExpandFolder(object s,RoutedEventArgs e){if((s as FrameworkElement)?.DataContext is FolderRow row)Model?.ToggleFolder(row.Id);e.Handled=true;}
    private static ListBoxItem? Item(object sender,DependencyObject source)=>ItemsControl.ContainerFromElement((ItemsControl)sender,source) as ListBoxItem;
    private void SelectContext(object s,MouseButtonEventArgs e){if(Item(s,(DependencyObject)e.OriginalSource) is{} item){item.IsSelected=true;if(Model!=null)Model.FolderContext=s==FolderTree;}}
    private void BuildMenu(ContextMenu menu,bool folder)
    {
        if(Model is not{} vm)return;vm.FolderContext=folder;menu.Items.Clear();
        void Add(string label,ICommand command){menu.Items.Add(new MenuItem{Header=label,Command=command});}
        if(folder){Add("Nova subpasta · Ctrl+Shift+N",vm.NewFolderCommand);Add("Renomear pasta · F2",vm.RenameItemCommand);Add("Mover pasta · Ctrl+Shift+M",vm.MoveItemCommand);Add("Excluir pasta…",vm.DeleteFolderCommand);}
        else{Add("Abrir",vm.OpenCommand);Add("Renomear texto · F2",vm.RenameItemCommand);Add("Mover / retirar da pasta · Ctrl+Shift+M",vm.MoveItemCommand);Add("Excluir documento…",vm.DeleteCommand);}
    }
    private void FolderMenuOpened(object s,RoutedEventArgs e)=>BuildMenu((ContextMenu)s,true);
    private void DocumentMenuOpened(object s,RoutedEventArgs e)=>BuildMenu((ContextMenu)s,false);
    private void FolderKey(object s,KeyEventArgs e)
    {
        if(Model is not{} vm||vm.SelectedFolder is not{} row||Keyboard.Modifiers!=ModifierKeys.None)return;
        if(e.Key==Key.Right){vm.ToggleFolder(row.Id,true);e.Handled=true;}
        else if(e.Key==Key.Left){if(row.Expanded)vm.ToggleFolder(row.Id,false);else vm.SelectedFolder=vm.FolderRows.FirstOrDefault(f=>f.Id==row.ParentId);e.Handled=true;}
    }
    private void BeginDrag(object s,MouseButtonEventArgs e){dragStart=e.GetPosition(this);if(Item(s,(DependencyObject)e.OriginalSource)!=null&&Model!=null)Model.FolderContext=s==FolderTree;}
    private void DragItem(object s,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed||Model==null||Item(s,(DependencyObject)e.OriginalSource)==null)return;
        var p=e.GetPosition(this);if(Math.Abs(p.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        string? id=s==FolderTree?Model.SelectedFolder?.Id:Model.SelectedDocument?.Id;if(id==null)return;
        DragDrop.DoDragDrop(this,new DataObject("Leitor.LibraryItem",new DragReference(id,s==FolderTree)),DragDropEffects.Move);
    }
    private void DragOverFolder(object s,DragEventArgs e){e.Effects=e.Data.GetDataPresent("Leitor.LibraryItem")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;}
    private async void DropFolder(object s,DragEventArgs e)
    {
        if(e.Data.GetData("Leitor.LibraryItem") is not DragReference item||Model==null)return;
        var row=Item(FolderTree,(DependencyObject)e.OriginalSource)?.DataContext as FolderRow;if(row==null)return;
        try{await Model.MoveLibraryItemAsync(item.Id,item.Folder,row.Id);}catch(Exception ex){Model.Status=ex.Message;}e.Handled=true;
    }
    private async void DropRoot(object s,DragEventArgs e){if(e.Data.GetData("Leitor.LibraryItem") is DragReference item&&Model!=null){try{await Model.MoveLibraryItemAsync(item.Id,item.Folder,null);}catch(Exception ex){Model.Status=ex.Message;}e.Handled=true;}}
    private void RootDropClick(object s,RoutedEventArgs e){if(Model!=null){Model.SelectedFolder=null;Model.LibraryView="Textos sem pasta";}}
}