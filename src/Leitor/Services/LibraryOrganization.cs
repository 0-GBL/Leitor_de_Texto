using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Leitor.Core;
using Microsoft.Data.Sqlite;

namespace Leitor.Services;
public sealed record LibraryFolder(string Id,string Name,string? ParentId,DateTime Created,DateTime Modified);
public sealed record LibraryQuery(string Search="",string Tag="",string View="Todos os textos",string? FolderId=null,string Scope="Toda a biblioteca",string Sort="Modificação · recentes",int Page=0,int PageSize=100);
public sealed record LibraryPage(IReadOnlyList<Document> Items,int Total);
public sealed record FolderImpact(int Documents,int Subfolders);
public sealed partial class LibraryStore
{
    public string? MigrationBackup { get; private set; }
    private void Migrate(string directory)
    {
        string file=Path.Combine(directory,"library.db"); bool existed=File.Exists(file);
        using var mutex=new Mutex(false,"Local\\Leitor-schema-"+Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant()))));
        bool acquired=false;
        try
        {
            try { acquired=mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch(AbandonedMutexException){acquired=true;}
            if(!acquired) throw new IOException("A biblioteca está sendo atualizada por outra instância.");
            using var db=Connect(); using var cmd=db.CreateCommand(); cmd.CommandText="PRAGMA user_version"; long version=(long)cmd.ExecuteScalar()!;
            if(version>2) throw new InvalidOperationException("Biblioteca criada por versão mais recente.");
            if(version==2) return;
            if(existed)
            {
                string backup=Path.Combine(directory,"backups","pre-1.3.0-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+"-"+Guid.NewGuid().ToString("N"));
                AtomicFiles.EnsurePrivateDirectory(backup);
                byte[]? keyBefore=File.Exists(Path.Combine(directory,"vault.json"))?File.ReadAllBytes(Path.Combine(directory,"vault.json")):null;
                if(keyBefore!=null)AtomicFiles.Write(Path.Combine(backup,"vault.json"),keyBefore);
                using(var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(backup,"library.db"),Pooling=false}.ToString())){copy.Open();db.BackupDatabase(copy);}
                foreach(string name in new[]{"auth-state.bin","settings.json"}) if(File.Exists(Path.Combine(directory,name))) File.Copy(Path.Combine(directory,name),Path.Combine(backup,name));
                byte[]? keyAfter=File.Exists(Path.Combine(directory,"vault.json"))?File.ReadAllBytes(Path.Combine(directory,"vault.json")):null;
                if((keyBefore==null)!=(keyAfter==null)||keyBefore!=null&&!keyBefore.AsSpan().SequenceEqual(keyAfter))throw new IOException("O cofre mudou durante a cópia. Feche outras instâncias e tente novamente; o banco não foi migrado.");
                MigrationBackup=backup;
            }
            using var tx=db.BeginTransaction(); cmd.Transaction=tx;
            cmd.CommandText="""
                CREATE TABLE IF NOT EXISTS documents(id TEXT PRIMARY KEY,protected INTEGER NOT NULL,payload BLOB NOT NULL);
                CREATE TABLE folders(id TEXT PRIMARY KEY,name TEXT NOT NULL,parent TEXT REFERENCES folders(id) ON DELETE CASCADE,created TEXT NOT NULL,modified TEXT NOT NULL);
                CREATE INDEX documents_protection ON documents(protected,id);
                CREATE INDEX folders_parent ON folders(parent,name);
                CREATE TABLE document_catalog(id TEXT PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE ON UPDATE CASCADE,folder TEXT REFERENCES folders(id) ON DELETE SET NULL,title TEXT NOT NULL,tags TEXT NOT NULL,created TEXT NOT NULL,saved TEXT NOT NULL,progress REAL NOT NULL,completed INTEGER NOT NULL,valid INTEGER NOT NULL);
                CREATE INDEX catalog_folder ON document_catalog(folder);
                CREATE INDEX catalog_title ON document_catalog(title);
                CREATE INDEX catalog_saved ON document_catalog(saved DESC,id ASC);
                CREATE INDEX catalog_created ON document_catalog(created DESC,id ASC);
                CREATE INDEX catalog_progress ON document_catalog(progress,id);
                INSERT INTO document_catalog SELECT id,NULL,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.Title'),'Sem título') ELSE '' END,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.Tags'),'') ELSE '' END,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.Created'),json_extract(payload,'$.Saved'),'2000-01-01T00:00:00') ELSE '2000-01-01T00:00:00' END,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.Saved'),'2000-01-01T00:00:00') ELSE '2000-01-01T00:00:00' END,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.ProgressPercent'),0) ELSE 0 END,
                  CASE WHEN protected=0 AND json_valid(payload) THEN coalesce(json_extract(payload,'$.Completed'),0) ELSE 0 END,
                  CASE WHEN protected=1 OR json_valid(payload) THEN 1 ELSE 0 END FROM documents;
                PRAGMA user_version=2;
                """;
            cmd.ExecuteNonQuery(); tx.Commit();
        }
        finally{if(acquired)mutex.ReleaseMutex();}
    }
    private static void Catalog(SqliteConnection db,SqliteTransaction tx,Document d)
    {
        using var c=db.CreateCommand(); c.Transaction=tx;
        c.CommandText="INSERT INTO document_catalog VALUES($id,$folder,$title,$tags,$created,$saved,$progress,$completed,1) ON CONFLICT(id) DO UPDATE SET folder=$folder,title=$title,tags=$tags,created=$created,saved=$saved,progress=$progress,completed=$completed,valid=1";
        c.Parameters.AddWithValue("$id",d.Id); c.Parameters.AddWithValue("$folder",d.Protected?DBNull.Value:(object?)d.FolderId??DBNull.Value);
        c.Parameters.AddWithValue("$title",d.Protected?"":d.Title); c.Parameters.AddWithValue("$tags",d.Protected?"":d.Tags);
        c.Parameters.AddWithValue("$created",d.Protected?"2000-01-01T00:00:00":(d.Created??d.Saved).ToString("O")); c.Parameters.AddWithValue("$saved",d.Protected?"2000-01-01T00:00:00":d.Saved.ToString("O"));
        c.Parameters.AddWithValue("$progress",d.Protected?0:d.ProgressValue);c.Parameters.AddWithValue("$completed",!d.Protected&&d.Completed?1:0);c.ExecuteNonQuery();
    }
    private static DateTime CatalogDate(string value)=>DateTime.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var date)?date:DateTime.MinValue;
    private Document Decode(string id,bool secure,byte[] bytes)
    {
        if(secure&&!vault.Unlocked)return new(){Id=id,Title="Texto protegido",Protected=true,IsLocked=true};
        byte[]? plain=null;
        try
        {
            plain=secure?vault.Open(bytes,id):bytes; var d=JsonSerializer.Deserialize<Document>(plain)??throw new JsonException();
            if(d.Id!=id||d.Protected!=secure||d.Title==null||d.Content==null||d.Tags==null)throw new CryptographicException(); return d;
        }
        catch(Exception e)when(e is JsonException or CryptographicException or FormatException){return new(){Id=id,Title="Documento indisponível",Protected=secure,IsUnavailable=true};}
        finally{if(secure&&plain!=null)CryptographicOperations.ZeroMemory(plain);}
    }
    private string? FolderOf(string id){using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT folder FROM document_catalog WHERE id=$id";c.Parameters.AddWithValue("$id",id);return c.ExecuteScalar() as string;}
    public Document? Get(string id)
    {
        using var db=Connect(); using var c=db.CreateCommand(); c.CommandText="SELECT protected,payload FROM documents WHERE id=$id"; c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();if(!r.Read())return null;
        var d=Decode(id,r.GetBoolean(0),(byte[])r[1]);if(!d.Protected)d.FolderId=FolderOf(id);return d;
    }
    public bool HasProtected {get{using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT EXISTS(SELECT 1 FROM documents WHERE protected=1)";return (long)c.ExecuteScalar()!!=0;}}
    public List<LibraryFolder> Folders()
    {
        using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT id,name,parent,created,modified FROM folders ORDER BY name COLLATE NOCASE";using var r=c.ExecuteReader();var list=new List<LibraryFolder>();while(r.Read())list.Add(new(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),CatalogDate(r.GetString(3)),CatalogDate(r.GetString(4))));return list.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static HashSet<string> Descendants(IReadOnlyList<LibraryFolder> folders,string id)
    {
        var children=folders.Where(f=>f.ParentId!=null).ToLookup(f=>f.ParentId!);var result=new HashSet<string>();var stack=new Stack<string>();stack.Push(id);
        while(stack.TryPop(out string? next))if(result.Add(next))foreach(var c in children[next])stack.Push(c.Id);return result;
    }
    public static string FolderPath(IReadOnlyList<LibraryFolder> folders,string? id)
    {
        if(id==null)return "Minha biblioteca";var map=folders.ToDictionary(f=>f.Id);var names=new List<string>();var seen=new HashSet<string>();
        while(id!=null&&seen.Add(id)&&map.TryGetValue(id,out var f)){names.Add(f.Name);id=f.ParentId;}names.Reverse();return string.Join(" / ",names);
    }
    public string CreateFolder(string name,string? parent=null)
    {
        ValidateName(name);using var db=Connect();using var c=db.CreateCommand();string id=Guid.NewGuid().ToString("N");c.CommandText="INSERT INTO folders VALUES($id,$name,$parent,$now,$now)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$name",name.Trim());c.Parameters.AddWithValue("$parent",(object?)parent??DBNull.Value);c.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));c.ExecuteNonQuery();return id;
    }
    private static void ValidateName(string name){if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("Informe um nome para a pasta.");}
    public void RenameFolder(string id,string name){ValidateName(name);using var db=Connect();using var c=db.CreateCommand();c.CommandText="UPDATE folders SET name=$name,modified=$now WHERE id=$id";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$name",name.Trim());c.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));c.ExecuteNonQuery();}
    public void MoveFolder(string id,string? parent)
    {
        using var db=Connect();using var tx=db.BeginTransaction();using var c=db.CreateCommand();
        c.Transaction=tx;c.CommandText="WITH RECURSIVE subtree(id) AS (SELECT id FROM folders WHERE id=$id UNION SELECT f.id FROM folders f JOIN subtree s ON f.parent=s.id) SELECT COUNT(*) FROM subtree WHERE id=$parent";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$parent",(object?)parent??DBNull.Value);
        if((long)c.ExecuteScalar()!>0)throw new InvalidOperationException("Uma pasta não pode ser movida para si ou uma subpasta.");
        c.CommandText="UPDATE folders SET parent=$parent,modified=$now WHERE id=$id";c.Parameters.AddWithValue("$now",DateTime.Now.ToString("O"));c.ExecuteNonQuery();tx.Commit();
    }
    public void MoveDocument(string id,string? folder)
    {
        var d=Get(id)??throw new InvalidOperationException("Documento não encontrado.");if(d.IsLocked)throw new InvalidOperationException("Desbloqueie antes de mover um texto protegido.");if(d.IsUnavailable)throw new InvalidOperationException("Documento indisponível foi preservado.");
        if(folder!=null&&!Folders().Any(f=>f.Id==folder))throw new InvalidOperationException("Pasta de destino não encontrada.");d.FolderId=folder;SaveCore(d,false);
    }
    public LibraryPage Query(LibraryQuery q,CancellationToken token=default)
    {
        var folders=Folders();if(!vault.Unlocked&&string.IsNullOrWhiteSpace(q.Search))return QueryLocked(q,folders,token);var map=folders.ToDictionary(f=>f.Id); HashSet<string>? scope=q.FolderId!=null&&(q.View=="Pastas e subpastas"&&string.IsNullOrWhiteSpace(q.Search)||q.Scope=="Pasta e subpastas")?Descendants(folders,q.FolderId):null;
        bool unfiled=q.View=="Textos sem pasta";
        using var db=Connect();db.CreateFunction<string?,string?,bool>("contains_ci",(value,term)=>(value??"").Contains(term??"",StringComparison.CurrentCultureIgnoreCase));
        db.CreateFunction<string?,bool>("in_scope",f=>scope==null||f!=null&&scope.Contains(f));
        using var c=db.CreateCommand();c.CommandText="""
            SELECT d.id,c.title,c.tags,c.created,c.saved,c.progress,c.completed,c.folder,c.valid
            FROM documents d JOIN document_catalog c ON c.id=d.id
            WHERE d.protected=0 AND in_scope(c.folder) AND ($unfiled=0 OR c.folder IS NULL) AND ($search='' OR contains_ci(c.title,$search) OR CASE WHEN json_valid(d.payload) THEN contains_ci(json_extract(d.payload,'$.Content'),$search) ELSE 0 END)
            AND ($tag='' OR contains_ci(c.tags,$tag))
            """;c.Parameters.AddWithValue("$search",q.Search.Trim());c.Parameters.AddWithValue("$tag",q.Tag.Trim());c.Parameters.AddWithValue("$unfiled",unfiled?1:0);
        using var cancel=token.Register(()=>c.Cancel()); var rows=new List<Document>();
        bool Accept(Document d)=> (scope==null||d.FolderId!=null&&scope.Contains(d.FolderId))&&(!unfiled||d.FolderId==null)&&(string.IsNullOrWhiteSpace(q.Tag)||d.Tags.Split(',',StringSplitOptions.TrimEntries).Any(t=>t.Equals(q.Tag.Trim(),StringComparison.CurrentCultureIgnoreCase)));
        using(var r=c.ExecuteReader())while(r.Read())
        {
            token.ThrowIfCancellationRequested();var d=new Document{Id=r.GetString(0),Title=r.GetString(1),Tags=r.GetString(2),Created=CatalogDate(r.GetString(3)),Saved=CatalogDate(r.GetString(4)),ProgressPercent=r.GetDouble(5),Completed=r.GetBoolean(6),FolderId=r.IsDBNull(7)?null:r.GetString(7),IsSummary=true,IsUnavailable=!r.GetBoolean(8)};if(Accept(d))rows.Add(d);
        }
        using var secure=db.CreateCommand();secure.CommandText="SELECT id,payload FROM documents WHERE protected=1";using(var r=secure.ExecuteReader())while(r.Read())
        {
            token.ThrowIfCancellationRequested();var d=Decode(r.GetString(0),true,(byte[])r[1]);
            if(d.IsLocked){if(scope==null&&!unfiled&&string.IsNullOrWhiteSpace(q.Search)&&string.IsNullOrWhiteSpace(q.Tag)){d.IsSummary=true;rows.Add(d);}continue;}
            if(!Accept(d)||!string.IsNullOrWhiteSpace(q.Search)&&!d.Title.Contains(q.Search.Trim(),StringComparison.CurrentCultureIgnoreCase)&&!d.Content.Contains(q.Search.Trim(),StringComparison.CurrentCultureIgnoreCase))continue;
            d.Content="";d.SegmentDurations=null;d.IsSummary=true;rows.Add(d);
        }
        IOrderedEnumerable<Document> ordered=q.Sort switch{
            "Título · A–Z"=>rows.OrderBy(d=>d.Title,StringComparer.CurrentCultureIgnoreCase),"Título · Z–A"=>rows.OrderByDescending(d=>d.Title,StringComparer.CurrentCultureIgnoreCase),
            "Criação · antigos"=>rows.OrderBy(d=>d.Created??d.Saved),"Criação · recentes"=>rows.OrderByDescending(d=>d.Created??d.Saved),
            "Modificação · antigos"=>rows.OrderBy(d=>d.Saved),"Progresso · crescente"=>rows.OrderBy(d=>d.ProgressValue),"Progresso · decrescente"=>rows.OrderByDescending(d=>d.ProgressValue),_=>rows.OrderByDescending(d=>d.Saved)};
        var page=ordered.ThenBy(d=>d.Id,StringComparer.Ordinal).Skip(Math.Max(0,q.Page)*Math.Max(1,q.PageSize)).Take(Math.Max(1,q.PageSize)).ToList();
        foreach(var d in page)d.Location=d.IsLocked?"Localização protegida":FolderPath(folders,d.FolderId);return new(page,rows.Count);
    }
    private LibraryPage QueryLocked(LibraryQuery q,IReadOnlyList<LibraryFolder> folders,CancellationToken token)
    {
        using var db=Connect();db.CreateFunction<string?,string?,bool>("contains_ci",(v,t)=>(v??"").Contains(t??"",StringComparison.CurrentCultureIgnoreCase));
        db.CreateFunction<string?,string?,bool>("has_tag",(v,t)=>(v??"").Split(',',StringSplitOptions.TrimEntries).Any(x=>x.Equals(t??"",StringComparison.CurrentCultureIgnoreCase)));
        db.CreateCollation("LEITOR",(a,b)=>StringComparer.CurrentCultureIgnoreCase.Compare(a,b));
        string? folder=q.FolderId!=null&&(q.View=="Pastas e subpastas"&&string.IsNullOrWhiteSpace(q.Search)||q.Scope=="Pasta e subpastas")?q.FolderId:null;
        const string prefix="""
            WITH RECURSIVE subtree(id) AS (SELECT id FROM folders WHERE id=$folder UNION SELECT f.id FROM folders f JOIN subtree s ON f.parent=s.id)
            """;
        const string from="""
            FROM documents d JOIN document_catalog c ON c.id=d.id
            WHERE ($folder IS NULL OR c.folder IN (SELECT id FROM subtree))
            AND ($unfiled=0 OR c.folder IS NULL AND d.protected=0)
            AND (d.protected=0 OR $search='' AND $tag='' AND $folder IS NULL AND $unfiled=0)
            AND ($search='' OR contains_ci(c.title,$search) OR CASE WHEN json_valid(d.payload) THEN contains_ci(json_extract(d.payload,'$.Content'),$search) ELSE 0 END)
            AND ($tag='' OR has_tag(c.tags,$tag))
            """;
        using var c=db.CreateCommand();c.Parameters.AddWithValue("$folder",(object?)folder??DBNull.Value);c.Parameters.AddWithValue("$unfiled",q.View=="Textos sem pasta"?1:0);c.Parameters.AddWithValue("$search",q.Search.Trim());c.Parameters.AddWithValue("$tag",q.Tag.Trim());using var cancel=token.Register(()=>c.Cancel());
        c.CommandText=folder==null&&q.View!="Textos sem pasta"&&string.IsNullOrWhiteSpace(q.Tag)?"SELECT COUNT(*) FROM document_catalog":prefix+" SELECT COUNT(*) "+from;int total=Convert.ToInt32(c.ExecuteScalar());token.ThrowIfCancellationRequested();
        string sort=q.Sort switch{"Título · A–Z"=>"CASE WHEN d.protected=1 THEN 'Texto protegido' ELSE c.title END COLLATE LEITOR ASC","Título · Z–A"=>"CASE WHEN d.protected=1 THEN 'Texto protegido' ELSE c.title END COLLATE LEITOR DESC","Criação · antigos"=>"c.created ASC","Criação · recentes"=>"c.created DESC","Modificação · antigos"=>"c.saved ASC","Progresso · crescente"=>"c.progress ASC","Progresso · decrescente"=>"c.progress DESC",_=>"c.saved DESC"};
        c.CommandText=prefix+" SELECT d.id,d.protected,c.title,c.tags,c.created,c.saved,c.progress,c.completed,c.folder,c.valid "+from+" ORDER BY "+sort+",c.id LIMIT $take OFFSET $skip";c.Parameters.AddWithValue("$take",Math.Max(1,q.PageSize));c.Parameters.AddWithValue("$skip",(long)Math.Max(0,q.Page)*Math.Max(1,q.PageSize));
        var items=new List<Document>();using var r=c.ExecuteReader();var locations=new Dictionary<string,string>();
        while(r.Read())
        {
            token.ThrowIfCancellationRequested();bool locked=r.GetBoolean(1);string? parent=r.IsDBNull(8)?null:r.GetString(8);
            var d=new Document{Id=r.GetString(0),Protected=locked,IsLocked=locked,IsSummary=true,Title=locked?"Texto protegido":r.GetString(2),Tags=locked?"":r.GetString(3),Created=locked?null:CatalogDate(r.GetString(4)),Saved=locked?DateTime.MinValue:CatalogDate(r.GetString(5)),ProgressPercent=locked?null:r.GetDouble(6),Completed=!locked&&r.GetBoolean(7),FolderId=locked?null:parent,IsUnavailable=!r.GetBoolean(9)};
            if(locked)d.Location="Localização protegida";else if(parent==null)d.Location="Minha biblioteca";else{if(!locations.TryGetValue(parent,out string? location)){location=FolderPath(folders,parent);locations[parent]=location;}d.Location=location;}items.Add(d);
        }
        return new(items,total);
    }
    public Dictionary<string,int> FolderCounts()
    {
        var counts=new Dictionary<string,int>();using var db=Connect();using var c=db.CreateCommand();c.CommandText="SELECT folder,COUNT(*) FROM document_catalog c JOIN documents d ON d.id=c.id WHERE d.protected=0 AND folder IS NOT NULL GROUP BY folder";using(var r=c.ExecuteReader())while(r.Read())counts[r.GetString(0)]=r.GetInt32(1);
        if(vault.Unlocked){using var s=db.CreateCommand();s.CommandText="SELECT id,payload FROM documents WHERE protected=1";using var r=s.ExecuteReader();while(r.Read()){var d=Decode(r.GetString(0),true,(byte[])r[1]);if(d.FolderId!=null&&!d.IsUnavailable)counts[d.FolderId]=counts.GetValueOrDefault(d.FolderId)+1;}}
        return counts;
    }
    public FolderImpact Impact(string id)
    {
        if(HasProtected&&!vault.Unlocked)throw new InvalidOperationException("Desbloqueie os textos protegidos para conferir o conteúdo antes de excluir uma pasta.");
        var set=Descendants(Folders(),id);var counts=FolderCounts();return new(set.Sum(x=>counts.GetValueOrDefault(x)),Math.Max(0,set.Count-1));
    }
    public void DeleteFolder(string id,bool deleteContents,FolderImpact? expected=null)
    {
        using var db=Connect();using var tx=db.BeginTransaction();var impact=Impact(id);if(expected!=null&&impact!=expected)throw new InvalidOperationException("O conteúdo da pasta mudou. Confira as quantidades novamente.");var folderList=Folders();var set=Descendants(folderList,id);
        string? parent;using(var c=db.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT parent FROM folders WHERE id=$id";c.Parameters.AddWithValue("$id",id);parent=c.ExecuteScalar() as string;}
        // Protected associations are read only while unlocked and are never persisted as plaintext.
        var affected=new List<(string Id,bool Protected)>();using(var c=db.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT d.id,d.protected,d.payload,c.folder FROM documents d LEFT JOIN document_catalog c ON c.id=d.id";using var r=c.ExecuteReader();while(r.Read()){bool p=r.GetBoolean(1);var d=p?Decode(r.GetString(0),true,(byte[])r[2]):new Document{Id=r.GetString(0),FolderId=r.IsDBNull(3)?null:r.GetString(3)};if(d.IsUnavailable||d.IsLocked)throw new InvalidOperationException("Integridade ou sessão inválida; exclusão cancelada.");if(d.FolderId!=null&&(deleteContents?set.Contains(d.FolderId):d.FolderId==id))affected.Add((d.Id,p));}}
        foreach(var entry in affected)
        {
            using var c=db.CreateCommand();c.Transaction=tx;c.Parameters.AddWithValue("$id",entry.Id);
            if(deleteContents)c.CommandText="DELETE FROM documents WHERE id=$id";
            else if(entry.Protected){c.CommandText="SELECT payload FROM documents WHERE id=$id";var d=Decode(entry.Id,true,(byte[])c.ExecuteScalar()!);if(d.IsLocked||d.IsUnavailable)throw new InvalidOperationException("Integridade ou sessão inválida; exclusão cancelada.");d.FolderId=parent;byte[] plain=JsonSerializer.SerializeToUtf8Bytes(d);byte[] sealedBytes;try{sealedBytes=vault.Seal(plain,d.Id);}finally{CryptographicOperations.ZeroMemory(plain);}c.CommandText="UPDATE documents SET payload=$v WHERE id=$id";c.Parameters.AddWithValue("$v",sealedBytes);}
            else{c.CommandText="UPDATE document_catalog SET folder=$parent WHERE id=$id";c.Parameters.AddWithValue("$parent",(object?)parent??DBNull.Value);}
            c.ExecuteNonQuery();
        }
        using(var c=db.CreateCommand()){c.Transaction=tx;c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$parent",(object?)parent??DBNull.Value);if(!deleteContents){c.CommandText="UPDATE folders SET parent=$parent WHERE parent=$id";c.ExecuteNonQuery();}if(deleteContents){var children=folderList.ToLookup(f=>f.ParentId??"");var order=new List<string>();var pending=new Stack<string>();pending.Push(id);var seen=new HashSet<string>();while(pending.TryPop(out var next)){if(!seen.Add(next))throw new InvalidOperationException("Hierarquia inválida; exclusão cancelada.");order.Add(next);foreach(var child in children[next])pending.Push(child.Id);}order.Reverse();c.CommandText="DELETE FROM folders WHERE id=$id";foreach(var next in order){c.Parameters["$id"].Value=next;c.ExecuteNonQuery();}}else{c.CommandText="DELETE FROM folders WHERE id=$id";c.ExecuteNonQuery();}}tx.Commit();
    }
}
