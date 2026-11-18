using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Leitor.Core;
using Leitor.Services;
using Leitor.Presentation;

namespace Leitor.ViewModels;
public sealed record FolderRow(string Id,string Name,string? ParentId,int Depth,int Count,bool Expanded,bool HasChildren)
{
    public Thickness Indent=>new(Math.Min(Depth*12,72),0,0,0);
    public string Icon=>Depth==0?"Folder":"Subfolder";
    public string Expander=>HasChildren?(Expanded?"▾":"▸"):"·";
    public string ExpansionLabel=>HasChildren?$"{(Expanded?"Recolher":"Expandir")} {Name}":$"Sem subpastas em {Name}";
    public string Label=>$"{Name} ({Count})";
}
public sealed partial class MainViewModel
{
    private bool hasSavedCurrent,treeUpdating;
    private CancellationTokenSource? libraryWork,statisticsWork;
    private long libraryRevision,securityRevision;
    private int page,totalDocuments;
    private bool libraryBusy;
    private List<LibraryFolder> folderData=[];
    private Dictionary<string,int> folderCounts=[];
    private FolderRow? selectedFolder;
    private TextStatistics statistics=new();
    public bool EditorOwnsMetrics {get;set;}
    public bool FolderContext {get;set;}
    public ObservableCollection<FolderRow> FolderRows {get;}=[];
    public IReadOnlyList<LibraryFolder> LibraryFolders=>folderData;
    public string[] LibraryViews {get;}=["Todos os textos","Pastas e subpastas","Textos sem pasta"];
    public string[] LibrarySorts {get;}=["Título · A–Z","Título · Z–A","Criação · recentes","Criação · antigos","Modificação · recentes","Modificação · antigos","Progresso · crescente","Progresso · decrescente"];
    public string[] SearchScopes {get;}=["Toda a biblioteca","Pasta e subpastas"];
    public string LibraryView {get=>Settings.LibraryView;set{if(value==null||!LibraryViews.Contains(value)||value==Settings.LibraryView)return;Settings.LibraryView=value;Raise();Raise(nameof(ShowFolders));Raise(nameof(NoFolders));page=0;SavePreferences();ScheduleLibrarySearch();}}
    public string LibrarySort {get=>Settings.LibrarySort;set{if(value==null||!LibrarySorts.Contains(value)||value==Settings.LibrarySort)return;Settings.LibrarySort=value;Raise();page=0;SavePreferences();ScheduleLibrarySearch();}}
    public string LibraryScope {get=>Settings.LibraryScope;set{if(value==null||!SearchScopes.Contains(value)||value==Settings.LibraryScope)return;Settings.LibraryScope=value;Raise();page=0;SavePreferences();ScheduleLibrarySearch();}}
    public bool NoFolders=>ShowFolders&&FolderRows.Count==0;
    public bool ShowFolders=>LibraryView=="Pastas e subpastas";
    public bool LibraryBusy {get=>libraryBusy;private set=>Set(ref libraryBusy,value);}
    public string PageLabel=>$"{(totalDocuments==0?0:page*100+1)}–{Math.Min((page+1)*100,totalDocuments)} de {totalDocuments}";
    public string FolderBreadcrumb=>LibraryStore.FolderPath(folderData,SelectedFolder?.Id);
    public FolderRow? SelectedFolder
    {
        get=>selectedFolder;
        set {if(!Set(ref selectedFolder,value))return;if(treeUpdating)return;FolderContext=true;Settings.SelectedFolderId=value?.Id;Raise(nameof(FolderBreadcrumb));if(value!=null)LibraryView="Pastas e subpastas";page=0;SavePreferences();ScheduleLibrarySearch();}
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
        NewFolderCommand=new(async()=>{string? name=OrganizationDialogs.Name("Nova pasta","Nome da pasta");if(name==null)return;var id=await Task.Run(()=>library.CreateFolder(name,SelectedFolder?.Id));Settings.SelectedFolderId=id;Settings.ExpandedFolderIds.Add(SelectedFolder?.Id??"");LibraryView="Pastas e subpastas";await ReloadLibraryAsync();SavePreferences();},error);
        RenameItemCommand=new(RenameSelectedAsync,error);MoveItemCommand=new(MoveSelectedAsync,error);DeleteFolderCommand=new(DeleteSelectedFolderAsync,error);
        NextPageCommand=new(async()=>{if((page+1)*100<totalDocuments){page++;await RefreshDocumentsAsync();}},error);
        PreviousPageCommand=new(async()=>{if(page>0){page--;await RefreshDocumentsAsync();}},error);
        if(!LibraryViews.Contains(Settings.LibraryView))Settings.LibraryView=LibraryViews[0];if(!LibrarySorts.Contains(Settings.LibrarySort))Settings.LibrarySort=LibrarySorts[4];if(!SearchScopes.Contains(Settings.LibraryScope))Settings.LibraryScope=SearchScopes[0];
        Settings.ExpandedFolderIds??=[];
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
        while(stack.TryPop(out var entry)){var f=entry.Folder;if(!seen.Add(f.Id))continue;rows.Add(new(f.Id,f.Name,f.ParentId,entry.Depth,aggregate.GetValueOrDefault(f.Id),expanded.Contains(f.Id),children[f.Id].Any()));if(expanded.Contains(f.Id))foreach(var child in children[f.Id].Reverse())stack.Push((child,entry.Depth+1));}
        treeUpdating=true;try{FolderRows.Clear();foreach(var row in rows)FolderRows.Add(row);SelectedFolder=rows.FirstOrDefault(r=>r.Id==Settings.SelectedFolderId);}finally{treeUpdating=false;}
        Raise(nameof(FolderBreadcrumb));Raise(nameof(NoFolders));
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
            var q=new LibraryQuery(Search,TagFilter,LibraryView,SelectedFolder?.Id,LibraryScope,LibrarySort,page);
            var result=await Task.Run(()=>library.Query(q,request.Token),request.Token);
            if(version!=libraryRevision||closed)return;
            if(page>0&&page*100>=result.Total){page=Math.Max(0,(result.Total-1)/100);q=q with{Page=page};result=await Task.Run(()=>library.Query(q,request.Token),request.Token);if(version!=libraryRevision||closed)return;}
            string? selected=SelectedDocument?.Id;all=result.Items.ToList();totalDocuments=result.Total;
            for(int i=0;i<all.Count;i++){if(i<Documents.Count)Documents[i]=all[i];else Documents.Add(all[i]);}while(Documents.Count>all.Count)Documents.RemoveAt(Documents.Count-1);
            SelectedDocument=Documents.FirstOrDefault(d=>d.Id==selected);Raise(nameof(LibraryEmpty));Raise(nameof(LibraryCount));Raise(nameof(EmptyLibraryTitle));Raise(nameof(EmptyLibraryDescription));Raise(nameof(PageLabel));
        }
        catch(OperationCanceledException)when(request.IsCancellationRequested){}
        catch(Microsoft.Data.Sqlite.SqliteException)when(request.IsCancellationRequested){}
        finally{if(version==libraryRevision)LibraryBusy=false;if(ReferenceEquals(libraryWork,request))libraryWork=null;request.Dispose();}
    }
    private bool UpdateSavedSummary(Document d)
    {
        int index=Documents.ToList().FindIndex(x=>x.Id==d.Id);if(index<0||LibrarySort.StartsWith("Progresso")||LibrarySort=="Modificação · antigos"||LibrarySort=="Modificação · recentes"&&index!=0)return false;
        var summary=new Document{Id=d.Id,Title=d.Title,Tags=d.Tags,Created=d.Created,Saved=d.Saved,FolderId=d.FolderId,Protected=d.Protected,Completed=d.Completed,ProgressPercent=d.ProgressValue,Location=Documents[index].Location,IsSummary=true};
        Documents[index]=summary;if(index<all.Count)all[index]=summary;return true;
    }
    private async Task RenameSelectedAsync()
    {
        if(FolderContext&&SelectedFolder is{} folder){string? name=OrganizationDialogs.Name("Renomear pasta",folder.Name);if(name!=null){await Task.Run(()=>library.RenameFolder(folder.Id,name));await ReloadLibraryAsync();}return;}
        if(SelectedDocument is not{} item)return;if(item.Protected&&!SecurityDialog.Verify(Vault,"Renomear texto protegido"))return;
        string? title=OrganizationDialogs.Name("Renomear documento",item.Title);if(title==null)return;await SaveAsync();var d=await Task.Run(()=>library.Get(item.Id));if(d==null||d.IsUnavailable||d.IsLocked)return;
        d.Title=title;await storageGate.WaitAsync();try{await Task.Run(()=>library.Save(d));if(current.Id==d.Id){loading=true;Title=title;current.Title=title;loading=false;}}finally{storageGate.Release();}await RefreshDocumentsAsync();
    }
    private async Task MoveSelectedAsync()
    {
        string? id=FolderContext?SelectedFolder?.Id:SelectedDocument?.Id;if(id==null)return;bool folder=FolderContext;
        var result=OrganizationDialogs.Destination(folderData,folder?id:null);if(result.Accepted)await MoveLibraryItemAsync(id,folder,result.Id);
    }
    public async Task MoveLibraryItemAsync(string id,bool folder,string? destination)
    {
        if(!folder){var d=await Task.Run(()=>library.Get(id));if(d?.Protected==true&&!SecurityDialog.Verify(Vault,"Mover texto protegido"))return;}
        await SaveAsync();await storageGate.WaitAsync();try{await Task.Run(()=>{if(folder)library.MoveFolder(id,destination);else library.MoveDocument(id,destination);});if(!folder&&current.Id==id)current.FolderId=destination;}finally{storageGate.Release();}await ReloadLibraryAsync();SavePreferences();Status="Organização salva na biblioteca local.";
    }
    private async Task DeleteSelectedFolderAsync()
    {
        if(SelectedFolder is not{} folder)return;
        await SaveAsync();if(library.HasProtected&&!SecurityDialog.Verify(Vault,"Conferir textos protegidos antes de excluir pasta"))return;
        var impact=await Task.Run(()=>library.Impact(folder.Id));int action=OrganizationDialogs.DeleteFolder(folder.Name,impact);if(action==0)return;
        if(action==2&&MessageBox.Show($"Excluir definitivamente {impact.Documents} documentos e {impact.Subfolders} subpastas?","Excluir todo o conteúdo",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        await storageGate.WaitAsync();try{await Task.Run(()=>library.DeleteFolder(folder.Id,action==2,impact));var existing=await Task.Run(()=>library.Get(current.Id));if(existing==null&&hasSavedCurrent){await Player.LoadAsync([]);SetDocument(new());}else if(existing!=null)current.FolderId=existing.FolderId;Settings.SelectedFolderId=null;}finally{storageGate.Release();}await ReloadLibraryAsync();SavePreferences();
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