using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leitor.Services;

namespace Leitor;

public sealed class VoiceManager : Window
{
    private readonly VoiceDownloads downloads = new();
    private readonly ComboBox choices = new() { Margin = new(0, 10, 0, 10) };
    private readonly TextBox card = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox accept = new() { Content = "Li a ficha e aceito a licença deste modelo", Margin = new(0, 10, 0, 10) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly Button download = new() { Content = "Baixar voz selecionada" };
    private readonly Button install = new() { Content = "Instalar / reparar Piper local" };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? cardRequest;
    private CancellationTokenSource? operation;
    private bool busy, cardReady;
    public VoiceManager()
    {
        Title = "Vozes offline"; Width = 650; Height = 660; MinWidth = 550; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Vozes que ficam no seu computador", FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "A conexão é usada somente para instalar recursos. Nenhum texto da biblioteca é enviado. O Piper é GPL-3.0-or-later; as vozes têm licenças próprias.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 14) });
        panel.Children.Add(install);
        choices.ItemsSource = VoiceDownloads.Catalog;
        panel.Children.Add(choices); panel.Children.Add(card); panel.Children.Add(accept); panel.Children.Add(download); panel.Children.Add(status);
        choices.SelectionChanged += async (_, _) => await LoadCardAsync();
        install.Click += async (_, _) => await InstallAsync(); download.Click += async (_, _) => await DownloadAsync();
        choices.SelectedIndex = 0;
        Closing += (_, e) => { if (busy) { operation?.Cancel(); status.Text = "Cancelando operação; aguarde…"; e.Cancel = true; } };
        Closed += (_, _) => { lifetime.Cancel(); cardRequest?.Cancel(); lifetime.Dispose(); };
    }
    private async Task LoadCardAsync()
    {
        cardRequest?.Cancel(); cardRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var request = cardRequest;
        accept.IsChecked = false; cardReady = false; card.Text = "Consultando ficha oficial do modelo…";
        if (choices.SelectedItem is not VoiceModel model) return;
        try { string text = await downloads.LicenseAsync(model, request.Token); if (!request.IsCancellationRequested) { card.Text = text; cardReady = true; } }
        catch (OperationCanceledException) { }
        catch (Exception e) { card.Text = "A ficha de licença não pôde ser obtida. Conecte-se à internet para instalar novas vozes.\n" + e.Message; }
    }
    private void Busy(bool value)
    {
        busy = value; install.IsEnabled = download.IsEnabled = choices.IsEnabled = !value;
        if (value) operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        else { operation?.Dispose(); operation = null; }
    }
    private async Task InstallAsync()
    {
        Busy(true);
        try
        {
            var info = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "runtime", "Install-Piper.ps1"), "-Destination", AppPaths.Runtime }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            using var cancel = operation!.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = process.StandardError.ReadToEndAsync(operation.Token);
            while (await process.StandardOutput.ReadLineAsync(operation.Token) is { } line) status.Text = line;
            await process.WaitForExitAsync(operation.Token);
            string error = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException("Falha ao instalar os recursos. " + error);
            status.Text = "Piper instalado. Selecione e baixe uma voz abaixo.";
        }
        catch (OperationCanceledException) { status.Text = "Instalação cancelada. Você pode tentar novamente."; }
        catch (Exception e) { status.Text = e.Message; }
        finally { Busy(false); }
    }
    private async Task DownloadAsync()
    {
        if (!cardReady || accept.IsChecked != true || choices.SelectedItem is not VoiceModel model) { status.Text = "Leia e aceite a ficha de licença antes de baixar."; return; }
        Busy(true);
        try
        {
            await downloads.DownloadAsync(model, card.Text, AppPaths.Voices, new Progress<string>(message => status.Text = message), operation!.Token);
            status.Text = "Voz instalada. Feche esta janela e selecione a voz na tela principal.";
        }
        catch (OperationCanceledException) { status.Text = "Download cancelado."; }
        catch (Exception e) { status.Text = "Falha no download: " + e.Message; }
        finally { Busy(false); }
    }
}
