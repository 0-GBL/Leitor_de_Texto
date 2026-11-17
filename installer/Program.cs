using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Leitor.Security;

namespace LeitorSetup;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        if (args.Contains("--verify-payload"))
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip")!;
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (string name in new[] { "Leitor.exe", "Leitor.dll", "coreclr.dll", "PresentationFramework.dll", "runtime/piper_worker.py", "runtime/check_piper.py", "runtime/piper-requirements.txt", "runtime/download-manifest.json", "runtime/Install-Piper.ps1", "LICENSE.txt", "README.md" })
                    if (!zip.Entries.Any(x => x.FullName.Replace('\\', '/') == name)) return 2;
                foreach (var entry in zip.Entries.Where(x => x.Length > 0)) { using var data = entry.Open(); data.CopyTo(Stream.Null); }
                foreach (var entry in zip.Entries) SafeArchive.Resolve(Path.Combine(Path.GetTempPath(), "Leitor-verify"), entry.FullName);
                return 0;
            }
            catch { return 1; }
        }
        return new Application().Run(new SetupWindow());
    }
}
public sealed class SetupWindow : Window
{
    private readonly string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Leitor");
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 10) };
    private readonly Button install = new() { Content = "Instalar Leitor", Padding = new(18, 10, 18, 10), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly CheckBox desktop = new() { Content = "Criar atalho na área de trabalho", IsChecked = true, Margin = new(0, 12, 0, 20) };
    private bool busy;
    public SetupWindow()
    {
        Title = "Instalar Leitor"; Width = 580; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 15; Background = new SolidColorBrush(Color.FromRgb(245, 239, 228)); Foreground = new SolidColorBrush(Color.FromRgb(48, 40, 32));
        var panel = new StackPanel { Margin = new(32) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Leitor 1.2.1", FontSize = 34, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Seu tempo, seu ritmo, sua leitura", FontSize = 20, Margin = new(0, 6, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "Leitura local para Windows 10/11 de 64 bits. Inclui o .NET necessário. Vozes neurais são instaladas opcionalmente dentro do aplicativo.\n\nInstalação para este usuário, sem privilégios de administrador.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = destination, FontSize = 12, Margin = new(0, 16, 0, 4), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(desktop); panel.Children.Add(install); panel.Children.Add(status);
        install.Click += async (_, _) => await InstallAsync();
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private async Task InstallAsync()
    {
        busy = true; install.IsEnabled = false; bool shortcut = desktop.IsChecked == true;
        status.Text = "Instalando os arquivos…";
        try
        {
            string exe = Path.Combine(destination, "Leitor.exe");
            if (File.Exists(exe)) { using var probe = new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            await Task.Run(() =>
            {
                Directory.CreateDirectory(destination);
                using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip") ?? throw new IOException("Pacote do aplicativo ausente.");
                using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
                SafeArchive.Extract(zip, destination);
            });
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Leitor"); Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(startMenu, "Leitor.lnk"), exe);
            if (shortcut) CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Leitor.lnk"), exe);
            File.WriteAllText(Path.Combine(destination, "Desinstalar.ps1"), UninstallScript, System.Text.Encoding.UTF8);
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Leitor");
            key.SetValue("DisplayName", "Leitor"); key.SetValue("DisplayVersion", "1.2.1"); key.SetValue("Publisher", "Projeto Leitor"); key.SetValue("InstallLocation", destination); key.SetValue("DisplayIcon", exe);
            key.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(destination, "Desinstalar.ps1")}\"");
            key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
            status.Text = "Instalação concluída. Abra o Leitor pelo menu Iniciar ou pelo atalho. Seus textos ficam em %LOCALAPPDATA%\\Leitor.";
            install.Content = "Concluído";
        }
        catch (Exception e) { status.Text = "Não foi possível instalar. Feche o Leitor se ele estiver aberto e tente novamente.\n" + e.Message; install.IsEnabled = true; }
        finally { busy = false; }
    }
    private static void CreateShortcut(string path, string exe)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host indisponível.");
        dynamic shell = Activator.CreateInstance(type)!; dynamic link = shell.CreateShortcut(path);
        link.TargetPath = exe; link.WorkingDirectory = Path.GetDirectoryName(exe); link.Description = "Leitor de textos em áudio"; link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    private const string UninstallScript = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName PresentationFramework
        if ([System.Windows.MessageBox]::Show('Desinstalar o Leitor? A biblioteca e as vozes baixadas serão preservadas.', 'Desinstalar Leitor', 'YesNo', 'Question') -ne 'Yes') { exit }
        $expectedPath = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Leitor')).TrimEnd('\')
        $actualPath = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
        if ($actualPath -ne $expectedPath) { throw 'Diretório de instalação inesperado.' }
        $appExe = Join-Path $actualPath 'Leitor.exe'
        if (Test-Path -LiteralPath $appExe) {
            try { $probe = [IO.File]::Open($appExe, 'Open', 'ReadWrite', 'None'); $probe.Dispose() }
            catch { [System.Windows.MessageBox]::Show('Feche o Leitor antes de desinstalar.'); exit 1 }
        }
        $desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Leitor.lnk'
        $menuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'Leitor'
        if (Test-Path -LiteralPath $desktopLink) { Remove-Item -LiteralPath $desktopLink -Force }
        if (Test-Path -LiteralPath $menuFolder) {
            $resolvedMenu = [IO.Path]::GetFullPath($menuFolder)
            $allowedMenu = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('Programs')) 'Leitor'))
            if ($resolvedMenu -ne $allowedMenu) { throw 'Atalho inesperado.' }
            Remove-Item -LiteralPath $menuFolder -Recurse -Force
        }
        Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Leitor' -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $actualPath -Recurse -Force
        [System.Windows.MessageBox]::Show('Leitor desinstalado. Sua biblioteca permanece em %LOCALAPPDATA%\Leitor.')
        """;
}
