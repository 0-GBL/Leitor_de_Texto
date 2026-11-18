using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Leitor.ViewModels;
using Leitor.Presentation;

static partial class Program
{
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        var stack=new Stack<DependencyObject>();stack.Push(parent);while(stack.TryPop(out var item)){if(item is T match)yield return match;for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)stack.Push(VisualTreeHelper.GetChild(item,i));}
    }
    private static DispatcherTimer AnswerDialog(string title,Action<Window> answer)
    {
        var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};timer.Tick+=(_,_)=>{var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title==title);if(dialog!=null){timer.Stop();answer(dialog);}};timer.Start();return timer;
    }
    private static void ClickButton(Window window,string label)
    {var button=VisualChildren<Button>(window).First(b=>(b.Content as string)==label);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void VerifyLibraryInterface(string root)
    {
        var previous=SynchronizationContext.Current;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        if(Application.Current==null){var app=new Leitor.App(e=>uiFault=e);app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;}
        string directory=System.IO.Path.Combine(root,"library-ui");var vm=new MainViewModel(directory,new ControlledEngine());var window=new Leitor.MainWindow(vm){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000};window.Show();
        try
        {
            AwaitUI(vm.InitializeAsync());Check(!vm.Vault.Configured,"PIN ativado por padrão");
            var answer=AnswerDialog("Nova pasta",w=>{VisualChildren<TextBox>(w).First().Text="Formação";ClickButton(w,"Salvar");});vm.NewFolderCommand.Execute(null);PumpUntil(()=>vm.NewFolderCommand.CanExecute(null)&&vm.FolderRows.Count==1);answer.Stop();string parent=vm.SelectedFolder!.Id;
            answer=AnswerDialog("Nova pasta",w=>{VisualChildren<TextBox>(w).First().Text="Módulo";ClickButton(w,"Salvar");});vm.NewFolderCommand.Execute(null);PumpUntil(()=>vm.NewFolderCommand.CanExecute(null)&&vm.FolderRows.Count==2);answer.Stop();string child=vm.SelectedFolder!.Id;Check(vm.LibraryFolders.Single(f=>f.Id==child).ParentId==parent,"Subpasta não herdou seleção");
            vm.FolderContext=true;answer=AnswerDialog("Renomear pasta",w=>{VisualChildren<TextBox>(w).First().Text="Fundamentos";ClickButton(w,"Salvar");});vm.RenameItemCommand.Execute(null);PumpUntil(()=>vm.RenameItemCommand.CanExecute(null)&&vm.LibraryFolders.Any(f=>f.Name=="Fundamentos"));answer.Stop();
            vm.ToggleFolder(parent,false);Check(vm.SelectedFolder?.Id==parent&&!vm.FolderRows.Single(r=>r.Id==parent).Expanded,"Recolhimento não selecionou o ancestral");vm.ToggleFolder(parent,true);vm.SelectedFolder=vm.FolderRows.Single(r=>r.Id==child);
            vm.NewCommand.Execute(null);PumpUntil(()=>vm.Content==""&&!vm.Player.Playing);vm.Title="Documento fictício";vm.Content="Uma frase de exemplo.\r\n"+new string('a',400);AwaitUI(vm.SaveAsync());PumpUntil(()=>vm.StatisticsLabel.Contains("2 linhas lógicas"));Check(vm.StatisticsLabel.Contains("≈"),"Estimativa não identificada");var editor=(TextBox)window.FindName("EditorBox");editor.AppendText(" mais palavras");PumpUntil(()=>vm.Content.EndsWith(" mais palavras"));Check(vm.StatisticsLabel.Contains("2 linhas lógicas"),"Quebra visual alterou linhas lógicas");
            AwaitUI(vm.SaveAsync());Check(vm.SelectedFolder!.Count==1&&vm.FolderRows.Single(r=>r.Id==parent).Count==1,"Contador da pasta não atualizou");
            var panel=(LibraryPanel)window.FindName("Sidebar");var menu=new ContextMenu();typeof(LibraryPanel).GetMethod("BuildMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(panel,[menu,true]);Check(menu.Items.Count==4&&menu.Items.OfType<MenuItem>().All(i=>i.Command!=null),"Menu de contexto sem comandos acessíveis");
            var column=(ColumnDefinition)window.FindName("LibraryColumn");column.Width=new GridLength(350);window.UpdateLayout();var splitter=VisualChildren<GridSplitter>(window).Single();splitter.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Check(Math.Abs(new Leitor.Services.SettingsStore(directory).Load().LibraryWidth-350)<2,"Largura não persistiu");
            vm.FolderContext=true;answer=AnswerDialog("Mover para pasta",w=>{VisualChildren<ListBox>(w).First().SelectedIndex=0;ClickButton(w,"Mover");});vm.MoveItemCommand.Execute(null);PumpUntil(()=>vm.MoveItemCommand.CanExecute(null)&&vm.LibraryFolders.Single(f=>f.Id==child).ParentId==null);answer.Stop();
            answer=AnswerDialog("Excluir pasta",w=>{Check(VisualChildren<Button>(w).Count(b=>b.Content is string)>=3,"Opções de exclusão ausentes");ClickButton(w,"Cancelar");});vm.DeleteFolderCommand.Execute(null);PumpUntil(()=>vm.DeleteFolderCommand.CanExecute(null));answer.Stop();Check(vm.LibraryFolders.Any(f=>f.Id==child),"Cancelar excluiu a pasta");
            vm.Settings.Volume=0;vm.PlayCommand.Execute(null);PumpUntil(()=>vm.NarratedSegment!=null);vm.LibraryView="Todos os textos";vm.Search="exemplo";PumpUntil(()=>!vm.LibraryBusy);Check(vm.Player.Playing,"Pesquisa interrompeu áudio");vm.PlayCommand.Execute(null);PumpUntil(()=>vm.Player.Paused);
            vm.Vault.Create("173829");vm.IsProtected=true;AwaitUI(vm.SaveAsync());vm.LockSessionCommand.Execute(null);PumpUntil(()=>!vm.Vault.Unlocked&&vm.LockSessionCommand.CanExecute(null));Check(vm.Content==""&&vm.StatisticsLabel.Contains("0 caracteres")&&vm.Documents.Where(d=>d.Protected).All(d=>d.IsLocked),"Bloqueio deixou conteúdo/metadados privados na tela");
        }
        finally{window.Close();PumpUntil(()=>!window.IsVisible);SynchronizationContext.SetSynchronizationContext(previous);}
    }
}