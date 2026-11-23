using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Leitor.Core;
using Leitor.Services;
using Leitor.Presentation;

namespace Leitor.ViewModels;
public sealed record FolderRow(string Id,string Name,string? ParentId,int Depth,int Count,bool Expanded,bool HasChildren,string? Color=null)
{
    public Thickness Indent=>new(Math.Min(Depth*12,72),0,0,0);
    public string Icon=>"LibraryFolder";
    public string Expander=>HasChildren?(Expanded?"▾":"▸"):"";
    public string ExpansionLabel=>HasChildren?$"{(Expanded?"Recolher":"Expandir")} {Name}":$"Sem subpastas em {Name}";
    public string Label=>$"{Name} ({Count})";
    public bool HasColor => Color != null;
}
public sealed record LibraryEntry(LibraryFolder? Folder, Document? Document, string? Color)
{
    public string Id => Folder?.Id ?? Document!.Id;
    public string Name => Folder?.Name ?? Document!.Title;
    public string Icon => Folder != null ? "LibraryFolder" : Document!.Protected ? "Shield" : "Document";
    public bool HasColor => Color != null;
    public string Detail => Document switch
    {
        { IsUnavailable: true } unavailable => unavailable.ProtectionStatus,
        { IsLocked: false, Protected: true } document => $"{document.ProgressLabel} · {document.ProtectionStatus}",
        not null => Document.ProgressLabel,
        _ => "Pasta"
    };
    public string? Tooltip => Document is { IsLocked: false } document
        ? string.Join(" · ", new[] { document.Location, document.DateLabel, document.Tags }.Where(value => !string.IsNullOrWhiteSpace(value)))
        : null;
}
public sealed record LibraryCrumb(string? Id, string Name, bool Current = false);
public sealed partial class MainViewModel
{
    private bool hasSavedCurrent,treeUpdating;
    private CancellationTokenSource? libraryWork,statisticsWork;
    private long libraryRevision,securityRevision;
    private int page,totalDocuments;
    private bool libraryBusy;
    private List<LibraryFolder> folderData=[];
    private Dictionary<string,int> folderCounts=[];
    private List<Document> documents = [];
    private FolderRow? selectedFolder;
    private LibraryEntry? selectedEntry;
    private readonly Stack<string?> backFolders = new(), forwardFolders = new();
    private bool traversingHistory, libraryVisible = true, focusMode;
    private TextStatistics statistics=new();
    public bool EditorOwnsMetrics {get;set;}
    public bool FolderContext {get;set;}
    public ObservableCollection<FolderRow> FolderRows {get;}=[];
    public IReadOnlyList<Document> Documents => documents;
    public IReadOnlyList<LibraryFolder> LibraryFolders=>folderData;
    public string[] LibraryViews {get;}=["Pastas e subpastas","Todos os textos","Textos sem pasta","Recentes"];
    public string[] LibrarySorts {get;}=["Título · A–Z","Título · Z–A","Criação · recentes","Criação · antigos","Modificação · recentes","Modificação · antigos","Progresso · crescente","Progresso · decrescente"];
    public string[] SearchScopes {get;}=["Toda a biblioteca","Pasta e subpastas"];
    public string LibraryView {get=>Settings.LibraryView;set{if(value==null||!LibraryViews.Contains(value)||value==Settings.LibraryView)return;Settings.LibraryView=value;Raise();UpdateExplorer();page=0;SavePreferences();ScheduleLibrarySearch();}}
    public string LibrarySort {get=>Settings.LibrarySort;set{if(value==null||!LibrarySorts.Contains(value)||value==Settings.LibrarySort)return;Settings.LibrarySort=value;Raise();page=0;SavePreferences();ScheduleLibrarySearch();}}
    public string LibraryScope {get=>Settings.LibraryScope;set{if(value==null||!SearchScopes.Contains(value)||value==Settings.LibraryScope)return;Settings.LibraryScope=value;Raise();page=0;SavePreferences();ScheduleLibrarySearch();}}
    public bool LibraryVisible { get => libraryVisible; private set { if (Set(ref libraryVisible, value)) Raise(nameof(ReadingVisible)); } }
    public bool ReadingVisible => !LibraryVisible;
    public bool FocusMode { get => focusMode; private set { if (Set(ref focusMode, value)) Raise(nameof(EditingVisible)); } }
    public bool EditingVisible => !FocusMode;
    public Command ShowLibraryCommand { get; private set; } = null!;
    public Command ShowReaderCommand { get; private set; } = null!;
    public AsyncCommand ToggleFocusCommand { get; private set; } = null!;
    public Command NavigateBackCommand { get; private set; } = null!;
    public Command NavigateForwardCommand { get; private set; } = null!;
    public Command OpenFolderCommand { get; private set; } = null!;
    public AsyncCommand NewSubfolderCommand { get; private set; } = null!;
    public AsyncCommand CopyItemCommand { get; private set; } = null!;
    public AsyncCommand ActivateEntryCommand { get; private set; } = null!;
    public IReadOnlyList<LibraryEntry> Entries { get; private set; } = [];
    public bool ExplorerEmpty => Entries.Count == 0 && !LibraryBusy;
    public string DirectoryTitle => LibraryView == "Pastas e subpastas" ? SelectedFolder?.Name ?? "Biblioteca" : LibraryView;
    public string NewFolderLocation => $"Criar em {string.Join(" › ", Breadcrumbs.Select(crumb => crumb.Name))}";
    public IReadOnlyList<LibraryCrumb> Breadcrumbs { get; private set; } = [];
    public LibraryEntry? SelectedEntry
    {
        get => selectedEntry;
        set
        {
            if (!Set(ref selectedEntry, value)) return;
            SelectedDocument = value?.Document;
            if (value != null) FolderContext = value.Folder != null;
        }
    }
    public bool LibraryGrid { get => Settings.LibraryGrid; set { if (value == Settings.LibraryGrid) return; Settings.LibraryGrid = value; Raise(); SavePreferences(); } }
    public string[] Densities { get; } = ["Compacta", "Normal", "Ampla"];
    public string LibraryDensity
    {
        get => Settings.LibraryDensity;
        set { if (!Densities.Contains(value) || value == Settings.LibraryDensity) return; Settings.LibraryDensity = value; Raise(); Raise(nameof(EntryPadding)); SavePreferences(); }
    }
    public Thickness EntryPadding => new(12, LibraryDensity == "Compacta" ? 4 : LibraryDensity == "Ampla" ? 16 : 9, 12, LibraryDensity == "Compacta" ? 4 : LibraryDensity == "Ampla" ? 16 : 9);
    public double LibraryIconSize
    {
        get => Settings.LibraryIconSize;
        set { if (Math.Abs(Settings.LibraryIconSize - value) < .01) return; Settings.LibraryIconSize = value; Raise(); SavePreferences(); }
    }
    private LibraryFolder? TargetFolder => SelectedEntry?.Folder ?? folderData.FirstOrDefault(f => f.Id == SelectedFolder?.Id);
    public bool LibraryBusy {get=>libraryBusy;private set{if(Set(ref libraryBusy,value))Raise(nameof(ExplorerEmpty));}}
    public string PageLabel=>$"{(totalDocuments==0?0:page*100+1)}–{Math.Min((page+1)*100,totalDocuments)} de {totalDocuments}";
    public bool HasMultiplePages => totalDocuments > 100;
    public FolderRow? SelectedFolder
    {
        get=>selectedFolder;
        set
        {
            string? previous = selectedFolder?.Id;
            if (!Set(ref selectedFolder,value) || treeUpdating || previous == value?.Id) return;
            if (!traversingHistory) { backFolders.Push(previous); forwardFolders.Clear(); }
            SelectedEntry = null;
            FolderContext = true;
            Settings.SelectedFolderId = value?.Id;
            Settings.LibraryView = "Pastas e subpastas";
            Raise(nameof(LibraryView));
            page = 0;
            UpdateExplorer();
            SavePreferences();
            ScheduleLibrarySearch();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public AsyncCommand NewFolderCommand {get;private set;}=null!;
    public AsyncCommand RenameItemCommand {get;private set;}=null!;
    public AsyncCommand MoveItemCommand {get;private set;}=null!;
    public AsyncCommand DeleteFolderCommand {get;private set;}=null!;
    public AsyncCommand NextPageCommand {get;private set;}=null!;
    public AsyncCommand PreviousPageCommand {get;private set;}=null!;
    public string StatisticsLabel=>$"{statistics.Characters:N0} caracteres · {statistics.Words:N0} palavras · {statistics.Lines:N0} linhas lógicas · ≈ {Format(statistics.EstimatedSeconds(new(SelectedVoice,Settings.Speed,Settings.Pitch,Settings.SentencePause,Settings.ParagraphPause,Settings.HeadingPause)))}";
    private void InitializeOrganization()
    {
        Action<Exception> error=e=>Status=FailureMessage(e);
        ShowLibraryCommand = new(() => { LibraryVisible = true; FocusMode = false; });
        ShowReaderCommand = new(() => LibraryVisible = false);
        ToggleFocusCommand = new(async () =>
        {
            LibraryVisible = false;
            FocusMode = !FocusMode;
            if (!FocusMode) return;
            if (!string.IsNullOrWhiteSpace(Content)) await PrepareReadingAsync();
            if (FocusMode) SelectedTab = 1;
        }, error);
        NavigateBackCommand = new(() => NavigateHistory(backFolders, forwardFolders), () => backFolders.Count > 0);
        NavigateForwardCommand = new(() => NavigateHistory(forwardFolders, backFolders), () => forwardFolders.Count > 0);
        ActivateEntryCommand = new(async () =>
        {
            if (SelectedEntry?.Folder is {} folder) NavigateFolder(folder.Id);
            else if (SelectedEntry?.Document is {} document) await ChangeDocumentAsync(() => OpenAsync(document));
        }, error);
        CopyItemCommand = new(() => MoveSelectedAsync(copy: true), error);
        OpenFolderCommand = new(() => { if (TargetFolder is {} folder) NavigateFolder(folder.Id); });
        NewFolderCommand = new(() => CreateFolderAsync(SelectedFolder?.Id), error);
        NewSubfolderCommand = new(() => CreateFolderAsync(TargetFolder?.Id), error);
        RenameItemCommand=new(RenameSelectedAsync,error);MoveItemCommand=new(()=>MoveSelectedAsync(),error);DeleteFolderCommand=new(DeleteSelectedFolderAsync,error);
        NextPageCommand=new(async()=>{if((page+1)*100<totalDocuments){page++;await RefreshDocumentsAsync();}},error);
        PreviousPageCommand=new(async()=>{if(page>0){page--;await RefreshDocumentsAsync();}},error);
        if(!LibraryViews.Contains(Settings.LibraryView))Settings.LibraryView=LibraryViews[0];if(!LibrarySorts.Contains(Settings.LibrarySort))Settings.LibrarySort=LibrarySorts[4];if(!SearchScopes.Contains(Settings.LibraryScope))Settings.LibraryScope=SearchScopes[0];
        Settings.ExpandedFolderIds??=[];
    }
    private async Task CreateFolderAsync(string? parent)
    {
        string location = parent == null ? "Biblioteca" : $"Biblioteca › {LibraryStore.FolderPath(folderData, parent).Replace(" / ", " › ")}";
        string? name = OrganizationDialogs.Name("Nova pasta", "", location);
        if (name == null) return;
        string id = await Task.Run(() => library.CreateFolder(name, parent));
        await RefreshFoldersAsync();
        NavigateFolder(id);
        await RefreshDocumentsAsync();
    }
    public void NavigateFolder(string? id)
    {
        if (id != null && !folderData.Any(folder => folder.Id == id)) return;
        Search = "";
        LibraryView = "Pastas e subpastas";
        if (id == SelectedFolder?.Id) return;
        if (id != null && !FolderRows.Any(row => row.Id == id))
        {
            var folder = folderData.First(f => f.Id == id);
            SelectedFolder = new(folder.Id, folder.Name, folder.ParentId, 0, 0, false, false);
            RebuildFolderRows();
        }
        else SelectedFolder = FolderRows.FirstOrDefault(f => f.Id == id);
    }
    private void NavigateHistory(Stack<string?> from, Stack<string?> to)
    {
        while (from.TryPop(out var id))
        {
            if (id == SelectedFolder?.Id || id != null && !folderData.Any(folder => folder.Id == id)) continue;
            to.Push(SelectedFolder?.Id);
            traversingHistory = true;
            try { NavigateFolder(id); }
            finally { traversingHistory = false; }
            break;
        }
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
    private void UpdateExplorer()
    {
        var map = folderData.ToDictionary(folder => folder.Id);
        var path = new List<LibraryCrumb>();
        string? id = SelectedFolder?.Id;
        var seen = new HashSet<string>();
        while (id != null && seen.Add(id) && map.TryGetValue(id, out var folder))
        {
            path.Add(new(folder.Id, folder.Name));
            id = folder.ParentId;
        }
        path.Add(new(null, "Biblioteca"));
        path.Reverse();
        path[^1] = path[^1] with { Current = true };
        Breadcrumbs = path;
        Raise(nameof(Breadcrumbs));
        Raise(nameof(DirectoryTitle));
        Raise(nameof(NewFolderLocation));
        RefreshEntries();
    }
    private void RefreshEntries()
    {
        string term = Search.Trim();
        bool browsing = LibraryView == "Pastas e subpastas" && term.Length == 0;
        var scoped = term.Length != 0 && LibraryScope == "Pasta e subpastas" && SelectedFolder is {} currentFolder
            ? LibraryStore.Descendants(folderData, currentFolder.Id) : null;
        IEnumerable<LibraryFolder> folders = LibraryView == "Recentes" ? [] : folderData.Where(folder =>
            term.Length == 0 ? browsing && folder.ParentId == SelectedFolder?.Id
                             : (scoped == null || scoped.Contains(folder.Id)) && folder.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        var previous = SelectedEntry;
        Entries = folders.Select(folder => new LibraryEntry(folder, null, Settings.FolderColors.GetValueOrDefault(folder.Id)))
            .Concat(Documents.Where(document => !browsing || document.FolderId == SelectedFolder?.Id)
                .Select(document => new LibraryEntry(null, document, null))).ToList();
        Raise(nameof(Entries));
        SelectedEntry = Entries.FirstOrDefault(entry => entry.Id == previous?.Id && (entry.Folder != null) == (previous?.Folder != null));
        Raise(nameof(ExplorerEmpty));
    }
    public void ColorSelectedFolder(string? color)
    {
        if (TargetFolder is not {} folder || !FolderContext) return;
        if (color == null) Settings.FolderColors.Remove(folder.Id);
        else Settings.FolderColors[folder.Id] = color;
        RebuildFolderRows();
        SavePreferences();
    }
    private async Task RefreshFoldersAsync()
    {
        long security=securityRevision;var result=await Task.Run(()=>(library.Folders(),library.FolderCounts()));if(closed||security!=securityRevision)return;folderData=result.Item1;folderCounts=result.Item2;RebuildFolderRows();
    }
    public void ToggleFolder(string id,bool? expand=null)
    {
        var expanded=Settings.ExpandedFolderIds.ToHashSet();bool opening=expand??!expanded.Contains(id);if(opening)expanded.Add(id);else{expanded.Remove(id);if(SelectedFolder is{} selected&&selected.Id!=id&&LibraryStore.Descendants(folderData,id).Contains(selected.Id))SelectedFolder=FolderRows.FirstOrDefault(r=>r.Id==id);}Settings.ExpandedFolderIds=expanded.ToList();RebuildFolderRows();SavePreferences();
    }
    private void RebuildFolderRows()
    {
        var children=folderData.ToLookup(f=>f.ParentId??"");var expanded=Settings.ExpandedFolderIds.ToHashSet();
        var map=folderData.ToDictionary(f=>f.Id);var ancestors=new HashSet<string>();string? selected=Settings.SelectedFolderId;if(selected!=null&&!map.ContainsKey(selected))Settings.SelectedFolderId=null;while(selected!=null&&ancestors.Add(selected)&&map.TryGetValue(selected,out var item)){if(item.ParentId!=null)expanded.Add(item.ParentId);selected=item.ParentId;}Settings.ExpandedFolderIds=expanded.ToList();
        var aggregate=new Dictionary<string,int>(folderCounts);var order=new List<LibraryFolder>();var scan=new Stack<LibraryFolder>(children[""].Reverse());var seen=new HashSet<string>();
        while(scan.TryPop(out var f)){if(!seen.Add(f.Id))continue;order.Add(f);foreach(var child in children[f.Id])scan.Push(child);}
        for(int i=order.Count-1;i>=0;i--)if(order[i].ParentId is string parent)aggregate[parent]=aggregate.GetValueOrDefault(parent)+aggregate.GetValueOrDefault(order[i].Id);
        var rows=new List<FolderRow>();var stack=new Stack<(LibraryFolder Folder,int Depth)>();foreach(var f in children[""].Reverse())stack.Push((f,0));seen.Clear();
        while(stack.TryPop(out var entry)){var f=entry.Folder;if(!seen.Add(f.Id))continue;rows.Add(new(f.Id,f.Name,f.ParentId,entry.Depth,aggregate.GetValueOrDefault(f.Id),expanded.Contains(f.Id),children[f.Id].Any(),Settings.FolderColors.GetValueOrDefault(f.Id)));if(expanded.Contains(f.Id))foreach(var child in children[f.Id].Reverse())stack.Push((child,entry.Depth+1));}
        treeUpdating=true;try{FolderRows.Clear();foreach(var row in rows)FolderRows.Add(row);SelectedFolder=rows.FirstOrDefault(r=>r.Id==Settings.SelectedFolderId);}finally{treeUpdating=false;}
        UpdateExplorer();
    }
    private async void ScheduleLibrarySearch()
    {
        if(initialization==null||closed)return;
        try {page=0;await RefreshDocumentsAsync(true);}catch(OperationCanceledException){}catch(Exception e){Status=FailureMessage(e);}
    }
    public async Task RefreshDocumentsAsync(bool debounce=false)
    {
        libraryWork?.Cancel();var request=new CancellationTokenSource();libraryWork=request;long version=++libraryRevision;LibraryBusy=true;
        try
        {
            if(debounce)await Task.Delay(160,request.Token);
            var q=new LibraryQuery(Search,TagFilter,LibraryView,LibraryView=="Recentes"?null:SelectedFolder?.Id,LibraryScope,LibrarySort,page,
                DirectFolder: LibraryView=="Pastas e subpastas"&&string.IsNullOrWhiteSpace(Search),
                RecentIds: LibraryView=="Recentes"?Settings.RecentDocumentIds.ToArray():null);
            var result=await Task.Run(()=>library.Query(q,request.Token),request.Token);
            if(version!=libraryRevision||closed)return;
            if(page>0&&page*100>=result.Total){page=Math.Max(0,(result.Total-1)/100);q=q with{Page=page};result=await Task.Run(()=>library.Query(q,request.Token),request.Token);if(version!=libraryRevision||closed)return;}
            string? selected=SelectedDocument?.Id;
            totalDocuments=result.Total;
            documents = result.Items.ToList();
            RefreshEntries();
            SelectedDocument=Documents.FirstOrDefault(d=>d.Id==selected);
            Raise(nameof(PageLabel));
            Raise(nameof(HasMultiplePages));
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Microsoft.Data.Sqlite.SqliteException)when(request.IsCancellationRequested){}
        finally{if(version==libraryRevision)LibraryBusy=false;if(ReferenceEquals(libraryWork,request))libraryWork=null;request.Dispose();}
    }
    private bool UpdateSavedSummary(Document d)
    {
        int index=documents.FindIndex(x=>x.Id==d.Id);if(index<0||LibrarySort.StartsWith("Progresso")||LibrarySort=="Modificação · antigos"||LibrarySort=="Modificação · recentes"&&index!=0)return false;
        var summary=new Document{Id=d.Id,Title=d.Title,Tags=d.Tags,Created=d.Created,Saved=d.Saved,FolderId=d.FolderId,Protected=d.Protected,Completed=d.Completed,ProgressPercent=d.ProgressValue,Location=Documents[index].Location,IsSummary=true};
        documents[index]=summary;RefreshEntries();return true;
    }
    private async Task RenameSelectedAsync()
    {
        if(FolderContext&&TargetFolder is{} folder){string? name=OrganizationDialogs.Name("Renomear pasta",folder.Name);if(name!=null){await Task.Run(()=>library.RenameFolder(folder.Id,name));await ReloadLibraryAsync();}return;}
        if(SelectedDocument is not{} item)return;if(item.Protected&&!SecurityDialog.Verify(Vault,"Renomear texto protegido"))return;
        string? title=OrganizationDialogs.Name("Renomear documento",item.Title);if(title==null)return;await SaveAsync();var d=await Task.Run(()=>library.Get(item.Id));if(d==null||d.IsUnavailable||d.IsLocked)return;
        d.Title=title;await storageGate.WaitAsync();try{await Task.Run(()=>library.Save(d));if(current.Id==d.Id){loading=true;Title=title;current.Title=title;loading=false;}}finally{storageGate.Release();}await RefreshDocumentsAsync();
    }
    private async Task MoveSelectedAsync(bool copy=false)
    {
        string? id=FolderContext?TargetFolder?.Id:SelectedDocument?.Id;if(id==null)return;bool folder=FolderContext;
        var result=OrganizationDialogs.Destination(folderData,folder?id:null,copy);if(result.Accepted)await MoveLibraryItemAsync(id,folder,result.Id,copy);
    }
    public async Task MoveLibraryItemAsync(string id,bool folder,string? destination,bool copy=false)
    {
        if (folder && copy && library.HasProtected && !SecurityDialog.Verify(Vault,"Conferir textos protegidos antes de copiar pasta")) return;
        if(!folder){var d=await Task.Run(()=>library.Get(id));if(d?.Protected==true&&!SecurityDialog.Verify(Vault,copy?"Copiar texto protegido":"Mover texto protegido"))return;}
        await SaveAsync();
        await storageGate.WaitAsync();
        try
        {
            if (folder && copy)
            {
                var copies = await Task.Run(() => library.CopyFolder(id, destination));
                foreach (var (source, target) in copies)
                    if (Settings.FolderColors.TryGetValue(source, out var color)) Settings.FolderColors[target] = color;
            }
            else if (folder) await Task.Run(() => library.MoveFolder(id, destination));
            else if (copy) await Task.Run(() => library.CopyDocument(id, destination));
            else
            {
                await Task.Run(() => library.MoveDocument(id, destination));
                if (current.Id == id) current.FolderId = destination;
            }
        }
        finally { storageGate.Release(); }
        await ReloadLibraryAsync();
        SavePreferences();
        Status = copy ? "Cópia salva na biblioteca." : "Item movido.";
    }
    private async Task DeleteSelectedFolderAsync()
    {
        if(TargetFolder is not{} folder)return;
        await SaveAsync();if(library.HasProtected&&!SecurityDialog.Verify(Vault,"Conferir textos protegidos antes de excluir pasta"))return;
        var impact=await Task.Run(()=>library.Impact(folder.Id));int action=OrganizationDialogs.DeleteFolder(folder.Name,impact);if(action==0)return;
        if(action==2&&MessageBox.Show($"Excluir definitivamente {impact.Documents} documentos e {impact.Subfolders} subpastas?","Excluir todo o conteúdo",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        await storageGate.WaitAsync();try{await Task.Run(()=>library.DeleteFolder(folder.Id,action==2,impact));var existing=await Task.Run(()=>library.Get(current.Id));if(existing==null&&hasSavedCurrent){await Player.LoadAsync([]);SetDocument(new());}else if(existing!=null)current.FolderId=existing.FolderId;if(Settings.SelectedFolderId==folder.Id)Settings.SelectedFolderId=folder.ParentId;}finally{storageGate.Release();}await ReloadLibraryAsync();
        var existingFolders = folderData.Select(item => item.Id).ToHashSet();
        foreach (var id in Settings.FolderColors.Keys.Where(id => !existingFolders.Contains(id)).ToList()) Settings.FolderColors.Remove(id);
        SavePreferences();
    }
    public void ApplyEditorStatistics(string text,ICollection<TextChange> changes)
    {
        if(changes.Count==1){var change=changes.First();if(change.AddedLength+change.RemovedLength<=512&&statistics.Characters==text.Length-change.AddedLength+change.RemovedLength){statisticsWork?.Cancel();statistics.Replace(change.Offset,change.RemovedLength,text.Substring(change.Offset,change.AddedLength));Raise(nameof(StatisticsLabel));return;}}
        QueueStatistics(text);
    }
    private async void QueueStatistics(string text)
    {
        statisticsWork?.Cancel();if(text.Length==0){statistics=new();Raise(nameof(StatisticsLabel));return;}var request=new CancellationTokenSource();statisticsWork=request;
        try{await Task.Delay(180,request.Token);var result=await Task.Run(()=>new TextStatistics(text),request.Token);if(!request.IsCancellationRequested&&!closed){statistics=result;Raise(nameof(StatisticsLabel));}}
        catch(OperationCanceledException){}finally{if(ReferenceEquals(statisticsWork,request))statisticsWork=null;request.Dispose();}
    }
}
