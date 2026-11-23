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
    private static void EnsureTestApplication()
    {
        if (Application.Current != null) return;
        var app = new Leitor.App(error => uiFault = error);
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }
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
        EnsureTestApplication();
        string directory=System.IO.Path.Combine(root,"library-ui");var vm=new MainViewModel(directory,new ControlledEngine());var window=new Leitor.MainWindow(vm){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-20000};window.Show();
        try
        {
            AwaitUI(vm.InitializeAsync());Check(!vm.Vault.Configured,"PIN ativado por padrão");
            var answer=AnswerDialog("Nova pasta",w=>{Check(VisualChildren<TextBlock>(w).Any(t=>t.Text=="Localização: Biblioteca"),"Destino raiz ausente");VisualChildren<TextBox>(w).First().Text="Formação";ClickButton(w,"Salvar");});vm.NewFolderCommand.Execute(null);PumpUntil(()=>vm.NewFolderCommand.CanExecute(null)&&vm.FolderRows.Count==1);answer.Stop();string parent=vm.SelectedFolder!.Id;
            answer=AnswerDialog("Nova pasta",w=>{Check(VisualChildren<TextBlock>(w).Any(t=>t.Text=="Localização: Biblioteca › Formação"),"Destino da subpasta ausente");VisualChildren<TextBox>(w).First().Text="Módulo";ClickButton(w,"Salvar");});vm.NewFolderCommand.Execute(null);PumpUntil(()=>vm.NewFolderCommand.CanExecute(null)&&vm.FolderRows.Count==2);answer.Stop();string child=vm.SelectedFolder!.Id;Check(vm.LibraryFolders.Single(f=>f.Id==child).ParentId==parent,"Subpasta não herdou seleção");
            vm.FolderContext=true;answer=AnswerDialog("Renomear pasta",w=>{VisualChildren<TextBox>(w).First().Text="Fundamentos";ClickButton(w,"Salvar");});vm.RenameItemCommand.Execute(null);PumpUntil(()=>vm.RenameItemCommand.CanExecute(null)&&vm.LibraryFolders.Any(f=>f.Name=="Fundamentos"));answer.Stop();
            vm.ToggleFolder(parent,false);Check(vm.SelectedFolder?.Id==parent&&!vm.FolderRows.Single(r=>r.Id==parent).Expanded,"Recolhimento não selecionou o ancestral");vm.ToggleFolder(parent,true);vm.SelectedFolder=vm.FolderRows.Single(r=>r.Id==child);
            vm.NewCommand.Execute(null);PumpUntil(()=>vm.Content==""&&!vm.Player.Playing);vm.Title="Documento fictício";vm.Content="Uma frase de exemplo.\r\n"+new string('a',400);AwaitUI(vm.SaveAsync());PumpUntil(()=>vm.StatisticsLabel.Contains("2 linhas lógicas"));Check(vm.StatisticsLabel.Contains("≈"),"Estimativa não identificada");var editor=(TextBox)window.FindName("EditorBox");editor.AppendText(" mais palavras");PumpUntil(()=>vm.Content.EndsWith(" mais palavras"));Check(vm.StatisticsLabel.Contains("2 linhas lógicas"),"Quebra visual alterou linhas lógicas");
            AwaitUI(vm.SaveAsync());Check(vm.SelectedFolder!.Count==1&&vm.FolderRows.Single(r=>r.Id==parent).Count==1,"Contador da pasta não atualizou");
            vm.ShowLibraryCommand.Execute(null);
            var panel=(LibraryPanel)window.FindName("Sidebar");var menu=new ContextMenu();typeof(LibraryPanel).GetMethod("BuildMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(panel,[menu,true]);
            Check(menu.Items.OfType<MenuItem>().Count(i=>i.Command!=null)==6 && menu.Items.OfType<MenuItem>().Any(i=>i.HasItems),"Menu de contexto sem comandos ou cores acessíveis");
            var column=(ColumnDefinition)panel.FindName("LibraryColumn");column.Width=new GridLength(250);window.UpdateLayout();var splitter=VisualChildren<GridSplitter>(window).Single();splitter.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});Check(Math.Abs(new Leitor.Services.SettingsStore(directory).Load().LibraryWidth-250)<2,"Largura não persistiu");
            vm.FolderContext=true;answer=AnswerDialog("Mover para pasta",w=>{VisualChildren<ListBox>(w).First().SelectedIndex=0;ClickButton(w,"Mover");});vm.MoveItemCommand.Execute(null);PumpUntil(()=>vm.MoveItemCommand.CanExecute(null)&&vm.LibraryFolders.Single(f=>f.Id==child).ParentId==null);answer.Stop();
            answer=AnswerDialog("Excluir pasta",w=>{Check(VisualChildren<Button>(w).Count(b=>b.Content is string)>=3,"Opções de exclusão ausentes");ClickButton(w,"Cancelar");});vm.DeleteFolderCommand.Execute(null);PumpUntil(()=>vm.DeleteFolderCommand.CanExecute(null));answer.Stop();Check(vm.LibraryFolders.Any(f=>f.Id==child),"Cancelar excluiu a pasta");
            vm.Settings.Volume=0;vm.PlayCommand.Execute(null);PumpUntil(()=>vm.NarratedSegment!=null);vm.LibraryView="Todos os textos";vm.Search="exemplo";PumpUntil(()=>!vm.LibraryBusy);Check(vm.Player.Playing,"Pesquisa interrompeu áudio");vm.PlayCommand.Execute(null);PumpUntil(()=>vm.Player.Paused);
            vm.Vault.Create("173829");vm.IsProtected=true;AwaitUI(vm.SaveAsync());vm.LockSessionCommand.Execute(null);PumpUntil(()=>!vm.Vault.Unlocked&&vm.LockSessionCommand.CanExecute(null));Check(vm.Content==""&&vm.StatisticsLabel.Contains("0 caracteres")&&vm.Documents.Where(d=>d.Protected).All(d=>d.IsLocked),"Bloqueio deixou conteúdo/metadados privados na tela");
        }
        finally{window.Close();PumpUntil(()=>!window.IsVisible);SynchronizationContext.SetSynchronizationContext(previous);}
    }

    private static void VerifyExplorerInterface(string root)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        EnsureTestApplication();
        string directory = System.IO.Path.Combine(root, "explorer-ui");
        using var vault = new Leitor.Services.Vault(directory);
        var store = new Leitor.Services.LibraryStore(directory, vault);
        string folder = store.CreateFolder("Faculdade"), child = store.CreateFolder("Módulo 01", folder);
        var document = new Leitor.Core.Document { Title = "Banco de Dados", Content = "Estruturas armazenam os dados. Índices aceleram buscas. Transações controlam alterações.", FolderId = child };
        store.Save(document);
        store.CreateFolder("Formação Full Stack");
        store.CreateFolder("Idiomas");
        const string longFolder = "Materiais de arquitetura e fundamentos de sistemas distribuídos";
        store.CreateFolder(longFolder);
        store.Save(new() { Title = "Resumo da semana", Content = "Revisar as anotações e concluir o exercício.", ProgressPercent = 42 });
        var vm = new MainViewModel(directory, new ControlledEngine());
        var window = new Leitor.MainWindow(vm) { ShowActivated=false, ShowInTaskbar=false, Left=-20000, WindowStartupLocation=WindowStartupLocation.Manual };
        window.Show();
        try
        {
            AwaitUI(vm.InitializeAsync());
            var panel = (LibraryPanel)window.FindName("Sidebar");
            var entryList = (ListBox)panel.FindName("LibraryList");
            vm.SelectedEntry = vm.Entries.Single(entry => entry.Id == folder);
            vm.ColorSelectedFolder("#3D865F");
            vm.ActivateEntryCommand.Execute(null);
            vm.NavigateFolder(child);
            Check(vm.Breadcrumbs.Select(crumb => crumb.Name).SequenceEqual(["Biblioteca", "Faculdade", "Módulo 01"]), "Caminho clicável divergiu");
            vm.NavigateBackCommand.Execute(null);
            Check(vm.SelectedFolder?.Id == folder, "Voltar perdeu a pasta");
            vm.NavigateForwardCommand.Execute(null);
            Check(vm.SelectedFolder?.Id == child, "Avançar perdeu a subpasta");
            vm.Search = "faculdade";
            PumpUntil(() => !vm.LibraryBusy && vm.Entries.Any(entry => entry.Id == folder));
            vm.NavigateFolder(null);
            PumpUntil(() => !vm.LibraryBusy);
            window.Width = 1280; window.Height = 800;
            vm.LibraryGrid = false;
            window.UpdateLayout();
            Check(panel.IsVisible && entryList.ActualWidth > 500 && VisualChildren<VirtualizingStackPanel>(entryList).Any(), "Lista não está virtualizada no layout amplo");
            vm.LibraryGrid = true;
            window.UpdateLayout();
            var wideGrid = VisualChildren<WrapPanel>(entryList).Single();
            int wideColumns = (int)(entryList.ActualWidth / wideGrid.ItemWidth);
            Check(wideColumns >= 3, "A grade ampla não distribuiu as colunas");
            Check(VisualChildren<TextBlock>(entryList).Any(t=>t.Text==longFolder && t.TextWrapping==TextWrapping.Wrap && t.ActualHeight>=40), "Nome extenso não quebrou em linhas");
            CaptureExplorerWindow(window, "biblioteca-clara");
            vm.LibraryDensity = "Ampla";
            vm.LibraryIconSize = 32;
            vm.ToggleThemeCommand.Execute(null);
            window.Width = 800; window.Height = 640;
            window.UpdateLayout();
            var narrowGrid = VisualChildren<WrapPanel>(entryList).Single();
            Check((int)(entryList.ActualWidth / narrowGrid.ItemWidth) < wideColumns && entryList.ActualHeight > 180, "Grade não se adaptou à resolução mínima");
            var first = (ListBoxItem)entryList.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (ListBoxItem)entryList.ItemContainerGenerator.ContainerFromIndex(1);
            Check(first.TranslatePoint(new(), narrowGrid).Y == second.TranslatePoint(new(), narrowGrid).Y, "A barra de rolagem eliminou uma coluna da grade");
            CaptureExplorerWindow(window, "biblioteca-escura");
            var toggle = (Button)panel.FindName("SidebarToggle");
            double sidebarWidth = ((ColumnDefinition)panel.FindName("LibraryColumn")).ActualWidth;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(!((Border)panel.FindName("FolderSidebar")).IsVisible && new Leitor.Services.SettingsStore(directory).Load().LibrarySidebarCollapsed,
                "Painel aberto ou preferência não persistida");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Check(Math.Abs(((ColumnDefinition)panel.FindName("LibraryColumn")).ActualWidth-sidebarWidth)<1, "Expandir perdeu a largura do painel");

            vm.NavigateFolder(child);
            PumpUntil(() => !vm.LibraryBusy && vm.Entries.Any(entry => entry.Id == document.Id));
            vm.SelectedEntry = vm.Entries.Single(entry => entry.Id == document.Id);
            vm.ActivateEntryCommand.Execute(null);
            PumpUntil(() => vm.ActivateEntryCommand.CanExecute(null) && vm.ReadingVisible);
            Check(!panel.IsVisible && vm.Settings.RecentDocumentIds.First() == document.Id, "Abrir texto não separou a leitura ou registrou o acesso");
            vm.ToggleFocusCommand.Execute(null);
            PumpUntil(() => vm.ToggleFocusCommand.CanExecute(null) && vm.Segments.Count > 0);
            Check(vm.FocusMode && vm.SelectedTab == 1 && !vm.Player.Playing && !((TextBox)window.FindName("EditorBox")).IsVisible,
                "Modo foco deixou o editor visível ou iniciou áudio automaticamente");
            vm.Settings.Volume = 0;
            vm.PlayCommand.Execute(null);
            PumpUntil(() => vm.NarratedSegment != null);
            window.UpdateLayout();
            CaptureExplorerWindow(window, "modo-foco-escuro");
            vm.ToggleFocusCommand.Execute(null);
            Check(!vm.FocusMode && vm.Player.Playing && vm.NarratedSegment != null, "Sair do foco interrompeu a leitura");
            vm.ShowLibraryCommand.Execute(null);
            vm.LibraryView = "Recentes";
            PumpUntil(() => !vm.LibraryBusy && vm.Entries.Count == 1);
            Check(vm.Entries.Single().Id == document.Id, "Documento aberto não apareceu em recentes");
        }
        finally
        {
            window.Close();
            PumpUntil(() => !window.IsVisible);
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void CaptureExplorerWindow(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("LEITOR_QA_CAPTURES") is not { Length: > 0 } directory) return;
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        System.IO.Directory.CreateDirectory(directory);
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
