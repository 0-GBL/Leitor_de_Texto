using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        string dll = Path.GetFullPath(args[1]); var resolver = new AssemblyDependencyResolver(dll);
        AssemblyLoadContext.Default.Resolving += (_, name) => resolver.ResolveAssemblyToPath(name) is string p ? AssemblyLoadContext.Default.LoadFromAssemblyPath(p) : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            string? native = resolver.ResolveUnmanagedDllToPath(name);
            native ??= new[] { Path.Combine(Path.GetDirectoryName(dll)!, name + ".dll"), Path.Combine(Path.GetDirectoryName(dll)!, "runtimes", "win-x64", "native", name + ".dll") }.FirstOrDefault(File.Exists);
            return native == null ? IntPtr.Zero : NativeLibrary.Load(native);
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        if (args.Contains("--library")) { Console.WriteLine(JsonSerializer.Serialize(ProfileLibrary(assembly,args[0]),new JsonSerializerOptions{WriteIndented=true})); return; }
        if (args.Contains("--ui")) { Console.WriteLine(JsonSerializer.Serialize(ProfileUi(assembly, args[0]), new JsonSerializerOptions { WriteIndented = true })); return; }
        if (args.Contains("--study") || args.Contains("--soak")) { Console.WriteLine(JsonSerializer.Serialize(ProfileStudy(assembly, args[0], args.Contains("--soak")), new JsonSerializerOptions { WriteIndented = true })); return; }
        var processor = assembly.GetType("Leitor.Core.TextProcessor")!.GetMethod("Split")!;
        string text = string.Concat(Enumerable.Repeat("Um parágrafo com conteúdo. Outra frase para estudar.\r\n", 60_000));
        object Split() => processor.Invoke(null, [text, CancellationToken.None])!;
        _ = Split(); GC.Collect(); long allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var times = new List<double>(); object? segments = null;
        for (int i = 0; i < 5; i++) { var watch = Stopwatch.StartNew(); segments = Split(); times.Add(watch.Elapsed.TotalMilliseconds); }
        double allocated = (GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 5d / 1048576;
        var playbackType = assembly.GetType("Leitor.Services.Playback")!;
        var playback = Activator.CreateInstance(playbackType, new object?[] { null })!;
        ((Task)playbackType.GetMethod("LoadAsync")!.Invoke(playback, [segments, null])!).GetAwaiter().GetResult();
        ((Task)playbackType.GetMethod("RestoreAsync")!.Invoke(playback, [119_999, 0d, null])!).GetAwaiter().GetResult();
        var position = playbackType.GetProperty("Position")!; var duration = playbackType.GetProperty("Duration")!;
        var query = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) { _ = position.GetValue(playback); _ = duration.GetValue(playback); }
        double queries = query.Elapsed.TotalMilliseconds; ((IDisposable)playback).Dispose();
        var application = (Application)Activator.CreateInstance(assembly.GetType("Leitor.App")!)!;
        assembly.GetType("Leitor.App")!.GetMethod("InitializeComponent")!.Invoke(application, null);
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var apply = assembly.GetType("Leitor.Services.ThemeService")!.GetMethod("Apply")!;
        apply.Invoke(null, [true]); apply.Invoke(null, [false]);
        long themeAlloc = GC.GetTotalAllocatedBytes(true); var themeWatch = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++) apply.Invoke(null, [i % 2 == 0]);
        double themeTime = themeWatch.Elapsed.TotalMilliseconds;
        double themeBytes = (GC.GetTotalAllocatedBytes(true) - themeAlloc) / 1024d;
        Console.WriteLine(JsonSerializer.Serialize(new { version = args[0], characters = text.Length, sentences = 120_000, splitMedianMs = times.Order().ElementAt(2), splitAllocatedMb = allocated, timelineQueries1000Ms = queries, themeSwitches50Ms = themeTime, themeAllocatedKb = themeBytes }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static object ProfileLibrary(Assembly assembly,string version)
    {
        var result=new List<object>();var connectionType=Assembly.Load("Microsoft.Data.Sqlite").GetType("Microsoft.Data.Sqlite.SqliteConnection")!;
        foreach(int count in new[]{140,500,1000,5000,10000})
        {
            string directory=Path.Combine(Path.GetTempPath(),"Leitor-library-bench-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            using(var db=(IDisposable)Activator.CreateInstance(connectionType,["Data Source="+Path.Combine(directory,"library.db")+";Pooling=False"])!)
            {
                dynamic connection=db;connection.Open();dynamic tx=connection.BeginTransaction();dynamic cmd=connection.CreateCommand();cmd.Transaction=tx;cmd.CommandText="CREATE TABLE documents(id TEXT PRIMARY KEY,protected INTEGER NOT NULL,payload BLOB NOT NULL);PRAGMA user_version=1;";cmd.ExecuteNonQuery();cmd.CommandText="INSERT INTO documents VALUES($id,0,$payload)";cmd.Parameters.AddWithValue("$id","");cmd.Parameters.AddWithValue("$payload",Array.Empty<byte>());
                for(int i=0;i<count;i++){string id="doc-"+i;cmd.Parameters["$id"].Value=id;cmd.Parameters["$payload"].Value=JsonSerializer.SerializeToUtf8Bytes(new{Id=id,Title="Aula "+i,Tags="curso",Content=new string('x',2048),Saved=new DateTime(2026,1,1),Protected=false});cmd.ExecuteNonQuery();}tx.Commit();cmd.Dispose();tx.Dispose();
            }
            var vault=Activator.CreateInstance(assembly.GetType("Leitor.Services.Vault")!,[directory,null])!;var storeType=assembly.GetType("Leitor.Services.LibraryStore")!;
            var migration=Stopwatch.StartNew();object store=Activator.CreateInstance(storeType,[directory,vault])!;double upgradeMs=migration.Elapsed.TotalMilliseconds;
            var open=Stopwatch.StartNew();store=Activator.CreateInstance(storeType,[directory,vault])!;double normalOpenMs=open.Elapsed.TotalMilliseconds;
            var queryType=assembly.GetType("Leitor.Services.LibraryQuery");var times=new List<double>();long allocated=GC.GetTotalAllocatedBytes(true);object? items=null;
            for(int sample=0;sample<3;sample++)
            {
                var watch=Stopwatch.StartNew();
                if(queryType==null)items=storeType.GetMethod("Load")!.Invoke(store,null)!;
                else
                {
                    var constructor = queryType.GetConstructors().Single(candidate => candidate.GetParameters().All(parameter => parameter.HasDefaultValue));
                    object query = constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.DefaultValue).ToArray());
                    object page = storeType.GetMethod("Query")!.Invoke(store,[query,CancellationToken.None])!;
                    items = page.GetType().GetProperty("Items")!.GetValue(page)!;
                }
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            double allocatedMb=(GC.GetTotalAllocatedBytes(true)-allocated)/3d/1048576;int returned=0;long textCharacters=0;
            foreach(object doc in (System.Collections.IEnumerable)items!){returned++;textCharacters+=((string)doc.GetType().GetProperty("Content")!.GetValue(doc)!).Length;}
            GC.KeepAlive(items);((IDisposable)vault).Dispose();result.Add(new{documents=count,initializationOrMigrationMs=upgradeMs,reopenMs=normalOpenMs,listMedianMs=times.Order().ElementAt(1),listAllocatedMb=allocatedMb,returnedRows=returned,retainedContentCharacters=textCharacters});
        }
        return new{version,fixture="Documentos fictícios de 2048 caracteres, JSON 1.2.1, sem pastas; primeira página de até 100 resumos quando há catálogo",rows=result};
    }
    private static object ProfileStudy(Assembly assembly, string version, bool soak)
    {
        var splitMethod = assembly.GetType("Leitor.Core.TextProcessor")!.GetMethod("Split")!;
        var playbackType = assembly.GetType("Leitor.Services.Playback")!;
        var engineType = assembly.GetType("Leitor.Services.ISpeechEngine")!;
        var rows = new List<object>();
        foreach (int words in soak ? new[] { 50_000 } : new[] { 1000, 5000, 10000, 25000, 50000 })
        {
            string text = string.Concat(Enumerable.Repeat("Uma leitura local ajuda a estudar com calma e atenção.\n", words / 10));
            object Split() => splitMethod.Invoke(null, [text, CancellationToken.None])!;
            _ = Split(); var splits = new List<double>(); long allocated = GC.GetTotalAllocatedBytes(true); object segments = null!;
            for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); segments = Split(); splits.Add(sw.Elapsed.TotalMilliseconds); }
            double splitMb = (GC.GetTotalAllocatedBytes(true) - allocated) / 3d / 1048576;
            object proxy = DispatchProxy.Create(engineType, typeof(BenchSpeechProxy)); var speech = (BenchSpeechProxy)proxy;
            object player = Activator.CreateInstance(playbackType, [proxy])!;
            Task Call(string name, params object?[] values) => (Task)playbackType.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == values.Length && (name != "SeekAsync" || m.GetParameters()[0].ParameterType == typeof(double))).Invoke(player, values)!;
            double Number(string name) => (double)playbackType.GetProperty(name)!.GetValue(player)!;
            bool Flag(string name) => (bool)playbackType.GetProperty(name)!.GetValue(player)!;
            int Audible() => (int)playbackType.GetProperty("AudibleIndex")!.GetValue(player)!;
            void WaitReady() { var sw = Stopwatch.StartNew(); while (Audible() < 0) { if (!Flag("Playing") || sw.Elapsed > TimeSpan.FromSeconds(15)) throw new Exception("Áudio de teste não iniciou"); Thread.Sleep(2); } }
            playbackType.GetMethod("SetVolume")!.Invoke(player, [0d]);
            var loadWatch = Stopwatch.StartNew(); Call("LoadAsync", segments, null).GetAwaiter().GetResult(); double loadMs = loadWatch.Elapsed.TotalMilliseconds;
            var startWatch = Stopwatch.StartNew(); Call("StartAsync", 0, 0d).GetAwaiter().GetResult(); WaitReady(); double firstAudioMs = startWatch.Elapsed.TotalMilliseconds;
            playbackType.GetMethod("TogglePause")!.Invoke(player, null); Thread.Sleep(50);
            var process = Process.GetCurrentProcess(); process.Refresh(); long beforeMemory = process.WorkingSet64; double beforeCpu = process.TotalProcessorTime.TotalMilliseconds;
            int beforeCalls = speech.Calls; var navigation = Stopwatch.StartNew(); double total = Number("Duration");
            int rounds = 0;
            do
            {
                var tasks = Enumerable.Range(1, 20).Select(i => Call("SeekAsync", total * (.05 + .9 * i / 20d))).ToArray(); Task.WhenAll(tasks).GetAwaiter().GetResult(); rounds++;
                if (soak) Thread.Sleep(40);
            } while (soak && navigation.Elapsed < TimeSpan.FromSeconds(30));
            if (Flag("Playing")) WaitReady(); double burstMs = navigation.Elapsed.TotalMilliseconds;
            var stopWatch = Stopwatch.StartNew(); Call("StopAsync").GetAwaiter().GetResult(); double stopMs = stopWatch.Elapsed.TotalMilliseconds;
            process.Refresh(); double cpuMs = process.TotalProcessorTime.TotalMilliseconds - beforeCpu;
            ((IDisposable)player).Dispose(); GC.Collect(); GC.WaitForPendingFinalizers(); process.Refresh();
            rows.Add(new { words, sentences = words / 10, splitMedianMs = splits.Order().ElementAt(1), splitAllocatedMb = splitMb, loadMs, firstAudioMs, navigationRequests = rounds * 20, navigationMs = burstMs, synthesisCallsForNavigation = speech.Calls - beforeCalls, stopMs, cpuMsDuringNavigation = cpuMs, workingSetAfterMb = process.WorkingSet64 / 1048576d, workingSetChangeMb = (process.WorkingSet64 - beforeMemory) / 1048576d });
        }
        return new { version, engine = "WAV sintético local de 2 s; geração não cooperativa de 40 ms; volume zero", soak, rows };
    }
    private static object ProfileUi(Assembly assembly, string version)
    {
        var application = (Application)Activator.CreateInstance(assembly.GetType("Leitor.App")!)!;
        assembly.GetType("Leitor.App")!.GetMethod("InitializeComponent")!.Invoke(application, null);
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var start = Stopwatch.StartNew(); var window = (Window)Activator.CreateInstance(assembly.GetType("Leitor.MainWindow")!)!;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -20000;
        window.Show(); var ready = window.GetType().GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Pump(() => (bool)ready.GetValue(window)!); double startup = start.Elapsed.TotalMilliseconds;
        var settle = Stopwatch.StartNew(); Pump(() => settle.ElapsedMilliseconds >= 2000);
        var process = Process.GetCurrentProcess(); double beforeCpu = process.TotalProcessorTime.TotalMilliseconds; var idle = Stopwatch.StartNew();
        Pump(() => idle.ElapsedMilliseconds >= 2000); process.Refresh();
        double idleCpu = process.TotalProcessorTime.TotalMilliseconds - beforeCpu, memory = process.WorkingSet64 / 1048576d;
        window.Close(); Pump(() => !window.IsVisible);
        return new { version, startupReadyMs = startup, idleCpuMsIn2s = idleCpu, processWorkingSetMb = memory };
    }
    private static void Pump(Func<bool> done)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("A janela não terminou a operação.");
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
}

public class BenchSpeechProxy : DispatchProxy
{
    public int Calls;
    private static readonly byte[] wave = MakeWave();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Interlocked.Increment(ref Calls); return Generate();
    }
    private static async Task<byte[]> Generate() { await Task.Delay(40); return wave; }
    private static byte[] MakeWave()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); int bytes = 16_000 * 2 * 2;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16_000); writer.Write(32_000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]); return stream.ToArray();
    }
}
