using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.IO;
using Microsoft.Data.Sqlite;
using Leitor.Core;
using Leitor.Services;

static partial class Program
{
    private static void RunLibraryTests(string root)
    {
        Run("Biblioteca 2.0: navegação direta e documentos recentes respeitam o PIN", () =>
        {
            string dir = Path.Combine(root, "explorer");
            using var vault = new Vault(dir);
            vault.Create("173829");
            var store = new LibraryStore(dir, vault);
            string folder = store.CreateFolder("Curso");
            string child = store.CreateFolder("Módulo", folder);
            var rootDoc = new Document { Title = "Raiz", Content = "Origem" };
            var courseDoc = new Document { Title = "Direto", Content = "Origem", FolderId = folder };
            var nestedDoc = new Document { Title = "Aninhado", FolderId = child };
            var secret = new Document { Title = "Segredo", FolderId = child, Protected = true };
            foreach (var doc in new[] { rootDoc, courseDoc, nestedDoc, secret }) store.Save(doc);
            foreach (bool unlocked in new[] { true, false })
            {
                if (!unlocked) vault.Lock();
                Check(store.Query(new(FolderId: folder, DirectFolder: true)).Items.Single().Id == courseDoc.Id, "Navegação direta incluiu descendentes");
                var recent = store.Query(new(RecentIds: [nestedDoc.Id, secret.Id, rootDoc.Id, "excluído", nestedDoc.Id]));
                Check(recent.Total == 2 && recent.Items.Select(d => d.Id).SequenceEqual([nestedDoc.Id, rootDoc.Id]), "Recentes revelaram protegido ou perderam a ordem");
                var search = store.Query(new(Search: "origem", PageSize: 1));
                Check(search.Total == 2 && search.Items.Count == 1, "Total da pesquisa divergiu da paginação");
            }
            Check(store.Query(new(DirectFolder: true)).Items.All(d => d.Id == rootDoc.Id || d.IsLocked), "A raiz incluiu pastas filhas");
        });
        Run("Biblioteca 2.0: cópia de árvore e de textos mantém progresso, PIN e atomicidade", () =>
        {
            string dir = Path.Combine(root, "copies");
            using var vault = new Vault(dir);
            vault.Create("173829");
            var store = new LibraryStore(dir, vault);
            string parent = store.CreateFolder("Origem"), child = store.CreateFolder("Aula", parent);
            string target = store.CreateFolder("Destino");
            var source = new Document { Title = "Aula", Content = "Progresso", FolderId = child, Offset = 2.75, Protected = true };
            store.Save(source);
            vault.Lock();
            try { store.CopyFolder(parent, target); throw new Exception("Cópia aceitou cofre bloqueado"); } catch (InvalidOperationException) { }
            Check(store.Folders().Count == 3 && store.Load().Count == 1, "Cópia incompleta deixou registros");
            Check(vault.Unlock("173829"), "Falha no PIN");
            try { store.CopyFolder(parent, child); throw new Exception("Cópia aceitou descendente"); } catch (InvalidOperationException) { }
            var copies = store.CopyFolder(parent, target);
            Check(copies.Count == 2 && store.Folders().Single(f => f.Id == copies[child]).ParentId == copies[parent], "Estrutura da cópia ficou incorreta");
            var copy = store.Load().Single(d => d.FolderId == copies[child]);
            Check(copy.Id != source.Id && copy.Protected && copy.Content == source.Content && copy.Offset == 2.75, "Cópia perdeu conteúdo, progresso ou proteção");
            string second = store.CopyDocument(source.Id, null);
            Check(store.Get(second)!.FolderId == null && store.Get(source.Id)!.FolderId == child, "Cópia alterou a origem");
            vault.Lock();
            Check(store.Get(copy.Id)!.IsLocked && store.Get(second)!.IsLocked, "Cópia protegida ficou disponível com PIN bloqueado");
        });
        Run("Biblioteca 2.0: personalização é persistente e configurações antigas continuam legíveis", () =>
        {
            string dir = Path.Combine(root, "explorer-settings");
            var store = new SettingsStore(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{"SelectedFolderId":"antiga","Dark":true}""");
            var settings = store.Load();
            Check(settings.SelectedFolderId == "antiga" && settings.Dark && settings.LibraryDensity == "Normal", "Preferências antigas incompatíveis");
            settings.LibraryGrid = true;
            settings.LibraryDensity = "Ampla";
            settings.LibraryIconSize = 36;
            settings.FolderColors["antiga"] = "#3D865F";
            settings.RecentDocumentIds = ["a", "b", "a"];
            store.Save(settings);
            var reloaded = store.Load();
            Check(reloaded.LibraryGrid && reloaded.LibraryDensity == "Ampla" && reloaded.LibraryIconSize == 36
                && reloaded.FolderColors["antiga"] == "#3D865F" && reloaded.RecentDocumentIds.SequenceEqual(["a", "b"]), "Preferências novas não persistiram");
        });
        Run("Biblioteca 1.3: migração com backup, payload intacto e idempotência",()=>
        {
            string dir=Path.Combine(root,"migration");using var vault=new Vault(dir);vault.Create("173829");
            var old=new Document{Id="antigo",Title="Texto antigo",Content="Original\r\nsem perda.",Tags="legado",Position=7,Offset=2.75,Saved=new DateTime(2026,1,2,10,20,30)};
            var secure=new Document{Id="seguro",Title="Título sigiloso",Content="Conteúdo confidencial para o teste",Protected=true,Offset=1.25,Saved=old.Saved};
            byte[] Legacy(Document d){var json=JsonNode.Parse(JsonSerializer.Serialize(d))!.AsObject();json.Remove("FolderId");json.Remove("Created");return JsonSerializer.SerializeToUtf8Bytes(json);}
            byte[] original=Legacy(old),cipher=vault.Seal(Legacy(secure),secure.Id);
            using(var db=new SqliteConnection("Data Source="+Path.Combine(dir,"library.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TABLE documents(id TEXT PRIMARY KEY,protected INTEGER NOT NULL,payload BLOB NOT NULL); PRAGMA user_version=1; INSERT INTO documents VALUES('antigo',0,$plain),('seguro',1,$cipher);";c.Parameters.AddWithValue("$plain",original);c.Parameters.AddWithValue("$cipher",cipher);c.ExecuteNonQuery();}
            vault.Lock();var store=new LibraryStore(dir,vault);Check(store.MigrationBackup!=null&&File.Exists(Path.Combine(store.MigrationBackup,"vault.json")),"Backup completo ausente");
            using(var db=new SqliteConnection("Data Source="+Path.Combine(store.MigrationBackup!,"library.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="SELECT payload FROM documents WHERE id='seguro'";Check(((byte[])c.ExecuteScalar()!).SequenceEqual(cipher),"Backup expôs ou modificou a cifra");c.CommandText="PRAGMA user_version";Check((long)c.ExecuteScalar()! ==1,"Backup foi migrado");}
            var d=store.Get(old.Id)!;Check(d.Content==old.Content&&d.Offset==old.Offset&&d.Position==7&&d.FolderId==null&&d.Saved==old.Saved,"Dados anteriores alterados");
            Check(store.Query(new()).Items.All(d=>d.Content==""),"Listagem carregou conteúdo");Check(store.Query(new(Search:"confidencial")).Total==0,"Pesquisa expôs texto protegido");
            var again=new LibraryStore(dir,vault);Check(again.MigrationBackup==null&&again.Query(new()).Total==2,"Migração duplicou banco/documentos");Check(vault.Unlock("173829")&&again.Get(secure.Id)!.Content==secure.Content,"PIN/cifra incompatíveis");
        });
        Run("Biblioteca 1.3: pastas, subpastas, nomes parametrizados e ciclos",()=>
        {
            using var vault=new Vault(Path.Combine(root,"folders"));var store=new LibraryStore(Path.Combine(root,"folders"),vault);
            string a=store.CreateFolder("Formação"),b=store.CreateFolder("Módulo 01",a),c=store.CreateFolder("Aula 01",b),literal=store.CreateFolder("O'Reilly'; DROP TABLE folders; --");
            store.RenameFolder(b,"Fundamentos");Check(store.Folders().Single(f=>f.Id==b).Name=="Fundamentos","Renomeação não persistiu");
            try{store.MoveFolder(a,c);throw new Exception("Ciclo aceito");}catch(InvalidOperationException){}
            try{store.MoveFolder(a,a);throw new Exception("Autorreferência aceita");}catch(InvalidOperationException){}
            store.MoveFolder(b,literal);Check(LibraryStore.FolderPath(store.Folders(),c).Contains("Fundamentos"),"Hierarquia incorreta após mover");store.MoveFolder(b,null);Check(store.Folders().Single(f=>f.Id==b).ParentId==null,"Não moveu para raiz");
        });
        Run("Biblioteca 1.3: mover documentos, retirar e preservar progresso/criação",()=>
        {
            string dir=Path.Combine(root,"moves");using var vault=new Vault(dir);var store=new LibraryStore(dir,vault);var d=new Document{Title="Aula",Content="Texto de teste",Offset=2.123,Position=4,ProgressPercent=43,Saved=new DateTime(2026,2,3)};store.Save(d);string folder=store.CreateFolder("Curso");store.MoveDocument(d.Id,folder);
            var moved=store.Get(d.Id)!;Check(moved.FolderId==folder&&moved.Offset==d.Offset&&moved.ProgressValue==43,"Movimentação perdeu dados");d.Title="Título editado no documento já aberto";store.Save(d);Check(store.Get(d.Id)!.FolderId==folder,"Autosave antigo desfez organização");store.MoveDocument(d.Id,null);Check(store.Get(d.Id)!.FolderId==null&&store.Get(d.Id)!.Created==new DateTime(2026,2,3),"Retirada perdeu data de criação");
        });
        Run("Biblioteca 1.3: exclusão somente pasta promove conteúdo; exclusão total é transacional",()=>
        {
            string dir=Path.Combine(root,"folder-delete");using var vault=new Vault(dir);var store=new LibraryStore(dir,vault);string parent=store.CreateFolder("Superior"),folder=store.CreateFolder("Pasta",parent),sub=store.CreateFolder("Subpasta",folder);
            Document a=new Document{Content="A",FolderId=folder},b=new Document{Content="B",FolderId=sub};store.Save(a);store.Save(b);Check(store.Impact(folder)==new FolderImpact(2,1),"Contagens incorretas");
            store.DeleteFolder(folder,false);Check(store.Get(a.Id)!.FolderId==parent&&store.Folders().Single(f=>f.Id==sub).ParentId==parent&&store.Get(b.Id)!=null,"Promoção perdeu conteúdo");
            using(var db=new SqliteConnection("Data Source="+Path.Combine(dir,"library.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="CREATE TRIGGER fail_delete BEFORE DELETE ON documents BEGIN SELECT RAISE(ABORT,'teste'); END;";c.ExecuteNonQuery();try{store.DeleteFolder(parent,true);throw new Exception("Falha ignorada");}catch(SqliteException){}Check(store.Get(a.Id)!=null&&store.Get(b.Id)!=null&&store.Folders().Any(f=>f.Id==parent),"Transação parcial");c.CommandText="DROP TRIGGER fail_delete";c.ExecuteNonQuery();}
            try{store.DeleteFolder(parent,true,new(0,0));throw new Exception("Confirmação desatualizada aceita");}catch(InvalidOperationException){}store.DeleteFolder(parent,true,store.Impact(parent));Check(store.Get(a.Id)==null&&store.Get(b.Id)==null&&!store.Folders().Any(),"Exclusão total incompleta");
        });
        Run("Biblioteca 1.3: organização protegida não expõe índices ou localização",()=>
        {
            string dir=Path.Combine(root,"protected-folders");using var vault=new Vault(dir);vault.Create("173829");var store=new LibraryStore(dir,vault);string folder=store.CreateFolder("Curso"),sub=store.CreateFolder("Sub",folder);var d=new Document{Title="Segredo exclusivo",Content="Conteúdo sigiloso",Tags="particular",Protected=true,FolderId=sub,Offset=3.14};store.Save(d);vault.Lock();
            Check(store.Query(new(Search:"sigiloso")).Total==0&&store.Query(new(View:"Textos sem pasta")).Total==0&&store.FolderCounts().Count==0,"Metadado protegido exposto");
            try{store.MoveDocument(d.Id,folder);throw new Exception("Moveu sem autenticação");}catch(InvalidOperationException){}try{store.DeleteFolder(folder,true);throw new Exception("Excluiu sem autenticação");}catch(InvalidOperationException){}
            d.Protected=false;try{store.Save(d);throw new Exception("Sobrescreveu protegido bloqueado");}catch(InvalidOperationException){}d.Protected=true;Check(store.Get(d.Id)!.IsLocked,"Gravação alterou o documento bloqueado");
            Check(vault.Unlock("173829"),"Falha no PIN");Check(store.Query(new(Search:"sigiloso",View:"Pastas e subpastas",FolderId:folder,Scope:"Pasta e subpastas")).Total==1,"Pesquisa protegida autorizada falhou");store.MoveDocument(d.Id,folder);store.DeleteFolder(folder,false);Check(store.Get(d.Id)!.FolderId==null&&store.Get(d.Id)!.Offset==3.14,"Promoção protegida perdeu progresso");
            using(var db=new SqliteConnection("Data Source="+Path.Combine(dir,"library.db"))){db.Open();using var c=db.CreateCommand();c.CommandText="SELECT title,tags,folder,progress FROM document_catalog WHERE id=$id";c.Parameters.AddWithValue("$id",d.Id);using var r=c.ExecuteReader();Check(r.Read()&&r.GetString(0)==""&&r.GetString(1)==""&&r.IsDBNull(2)&&r.GetDouble(3)==0,"Catálogo revelou detalhes protegidos");}
        });
        Run("Biblioteca 1.3: pesquisa combinada, escopos, ordenação e paginação",()=>
        {
            string dir=Path.Combine(root,"search-folders");using var vault=new Vault(dir);var store=new LibraryStore(dir,vault);string a=store.CreateFolder("A"),b=store.CreateFolder("B",a),outside=store.CreateFolder("Outra");
            foreach(var item in new[]{new Document{Title="Zebra",Content="Ação em camadas",Tags="curso, lógica",FolderId=b,ProgressPercent=30},new Document{Title="Ação",Content="Texto geral",Tags="curso",FolderId=outside,ProgressPercent=80},new Document{Title="Livre",Content="Texto solto",Tags="curso"}})store.Save(item);
            Check(store.Query(new(Search:"AÇÃO",Tag:"curso")).Total==2,"Pesquisa acentuada falhou");Check(store.Query(new(Search:"ação",Tag:"lógica",View:"Pastas e subpastas",FolderId:a,Scope:"Pasta e subpastas")).Total==1,"Filtros não combinam");
            Check(store.Query(new(Search:"ação",View:"Pastas e subpastas",FolderId:a,Scope:"Toda a biblioteca")).Total==2,"Busca global ficou presa à pasta");Check(store.Query(new(View:"Textos sem pasta")).Total==1,"Sem pasta incorreto");
            var page=store.Query(new(Sort:"Progresso · decrescente",Page:1,PageSize:1));Check(page.Total==3&&page.Items.Single().Title=="Zebra"&&page.Items.All(d=>d.Content==""),"Ordenação/paginação incorreta");
        });
        Run("Biblioteca 1.3: hierarquia profunda sem recursão da aplicação ou exclusão SQLite em cascata",()=>
        {
            string dir=Path.Combine(root,"deep-folders");using var vault=new Vault(dir);var store=new LibraryStore(dir,vault);string first=store.CreateFolder("Raiz"),last=first;for(int i=0;i<1100;i++)last=store.CreateFolder("Nível "+i,last);
            var d=new Document{Content="Folha profunda",FolderId=last};store.Save(d);Check(store.Impact(first)==new FolderImpact(1,1100),"Hierarquia truncada");Check(store.Query(new(View:"Pastas e subpastas",FolderId:first)).Total==1,"Pesquisa profunda truncada");store.DeleteFolder(first,true);Check(store.Folders().Count==0&&store.Get(d.Id)==null,"Exclusão profunda falhou");
        });
        Run("Biblioteca 1.3: contadores incrementais equivalem à contagem integral",()=>
        {
            string text="Primeiro parágrafo.\r\nAção, números 3,50 e emoji 🦊.\nÚltima linha."+new string('a',15000);var stats=new TextStatistics(text);var random=new Random(1730);
            for(int i=0;i<250;i++){int at=random.Next(text.Length+1),remove=Math.Min(random.Next(4),text.Length-at);string added=new[]{" palavra ","\r","\n","","🦊","."}[random.Next(6)];stats.Replace(at,remove,added);text=text.Remove(at,remove).Insert(at,added);Check(stats.Characters==text.Length&&stats.Words==Regex.Matches(text,@"\S+").Count&&stats.Lines==(text.Length==0?0:Regex.Matches(text,@"\r\n|\r|\n").Count+1),"Edição incremental divergiu");}
            double slow=stats.EstimatedSeconds(new(null,1,0,.25,.65,1)),fast=stats.EstimatedSeconds(new(null,2,0,.25,.65,1));Check(fast<slow,"Velocidade não alterou estimativa");stats.Replace(0,stats.Characters,"");Check(stats.Characters==0&&stats.Words==0&&stats.Lines==0,"Vazio incorreto");
        });
        Run("Biblioteca 1.3: preferências de organização são persistentes",()=>
        {
            string dir=Path.Combine(root,"organization-settings");var settings=new Settings{LibraryView="Pastas e subpastas",LibrarySort="Título · Z–A",LibraryScope="Pasta e subpastas",SelectedFolderId="pasta",ExpandedFolderIds=["raiz","pasta"],LibraryWidth=370};new SettingsStore(dir).Save(settings);var read=new SettingsStore(dir).Load();Check(read.SelectedFolderId=="pasta"&&read.ExpandedFolderIds.Count==2&&read.LibraryWidth==370&&read.LibrarySort==settings.LibrarySort&&!read.AppLock,"Preferências incompatíveis");
        });
        Run("Biblioteca 1.3: atalhos de organização respeitam edição",()=>
        {
            Check(Leitor.Presentation.Shortcuts.ResolveLibrary(System.Windows.Input.Key.N,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift,false)=="new","Nova pasta indisponível");
            Check(Leitor.Presentation.Shortcuts.ResolveLibrary(System.Windows.Input.Key.F2,System.Windows.Input.ModifierKeys.None,false)=="rename"&&Leitor.Presentation.Shortcuts.ResolveLibrary(System.Windows.Input.Key.F2,System.Windows.Input.ModifierKeys.None,true)==null,"Renomeação conflita com edição");
            Check(Leitor.Presentation.Shortcuts.ResolveLibrary(System.Windows.Input.Key.M,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift,false)=="move","Mover sem alternativa de teclado");
        });
        Run("Biblioteca 1.3: escala 140 a 10.000 documentos, pesquisa, movimento e áudio simultâneo",()=>MeasureLibraries(root));
    }
    private static void MeasureLibraries(string root)
    {
        var results=new List<object>();
        foreach(int size in new[]{140,500,1000,5000,10000})
        {
            string dir=Path.Combine(root,"scale-"+size);using var vault=new Vault(dir);var store=new LibraryStore(dir,vault);var folderIds=new List<string>();for(int i=0;i<Math.Max(14,size/50);i++)folderIds.Add(store.CreateFolder("Pasta "+i,i>0&&i%8!=0?folderIds[^1]:null));
            using(var db=new SqliteConnection("Data Source="+Path.Combine(dir,"library.db"))){db.Open();using var tx=db.BeginTransaction();using var c=db.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO documents VALUES($id,0,$payload); INSERT INTO document_catalog VALUES($id,$folder,$title,'curso',$now,$now,$progress,0,1);";foreach(string name in new[]{"$id","$payload","$folder","$title","$now","$progress"})c.Parameters.Add(new SqliteParameter(name,null));for(int i=0;i<size;i++){var d=new Document{Id="doc-"+i,Title="Aula "+i.ToString("D5"),Tags="curso",Content=(i%97==0?"Especial ":"Comum ")+new string('x',2048),FolderId=folderIds[i%folderIds.Count],ProgressPercent=i%101};c.Parameters["$id"].Value=d.Id;c.Parameters["$payload"].Value=JsonSerializer.SerializeToUtf8Bytes(d);c.Parameters["$folder"].Value=d.FolderId!;c.Parameters["$title"].Value=d.Title;c.Parameters["$now"].Value=d.Saved.ToString("O");c.Parameters["$progress"].Value=d.ProgressValue;c.ExecuteNonQuery();}tx.Commit();}
            var process=Process.GetCurrentProcess();process.Refresh();double cpu=process.TotalProcessorTime.TotalMilliseconds;long allocated=GC.GetTotalAllocatedBytes(true);var watch=Stopwatch.StartNew();var page=store.Query(new());double open=watch.Elapsed.TotalMilliseconds;Check(page.Total==size&&page.Items.Count==Math.Min(100,size)&&page.Items.All(x=>x.Content==""),"Listagem não paginada/carregou conteúdo");
            watch.Restart();var search=store.Query(new(Search:"especial"));double find=watch.Elapsed.TotalMilliseconds;Check(search.Total==(size+96)/97,"Pesquisa incompleta");watch.Restart();var folders=store.Folders();var counts=store.FolderCounts();double tree=watch.Elapsed.TotalMilliseconds;Check(counts.Values.Sum()==size,"Contagem incorreta");
            using var audio=new Playback(new ControlledEngine{DelayMs=20});audio.SetVolume(0);audio.LoadAsync(TextProcessor.Split("Áudio de teste para organizar a biblioteca. Segunda frase.")).GetAwaiter().GetResult();audio.StartAsync().GetAwaiter().GetResult();WaitFor(()=>audio.AudibleIndex==0);watch.Restart();store.MoveDocument("doc-0",folderIds[^1]);var concurrent=store.Query(new(Search:"especial"));double move=watch.Elapsed.TotalMilliseconds;Check(audio.Playing&&audio.AudibleIndex==0&&concurrent.Total==search.Total,"Organização interrompeu o áudio");audio.StopAsync().GetAwaiter().GetResult();process.Refresh();
            var row=new{documents=size,folders=folderIds.Count,openMs=open,searchMs=find,folderLoadMs=tree,moveAndSearchMs=move,allocatedMb=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,workingSetMb=process.WorkingSet64/1048576d,cpuMs=process.TotalProcessorTime.TotalMilliseconds-cpu};results.Add(row);Console.WriteLine("  escala: "+JsonSerializer.Serialize(row));
        }
        string? output=Environment.GetEnvironmentVariable("LEITOR_BENCH_OUTPUT");if(!string.IsNullOrWhiteSpace(output))File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
