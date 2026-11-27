using System.IO;
using System.Windows;
using System.Windows.Controls;
using Leitor.Core;
using Leitor.Services;
using Microsoft.Win32;

namespace Leitor;

internal sealed record ExportDocument(string Title, string Text, bool Legacy, SpeechOptions Options, string? Estimate);

internal sealed class ExportWindow : Window
{
    private readonly ExportDocument document;
    private readonly Button export = new() { Content = "Exportar", IsDefault = true };
    private readonly Button cancel = new() { Content = "Cancelar" };
    private readonly ProgressBar progress = new() { Height = 7, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
    private CancellationTokenSource? operation;

    public ExportWindow(ExportDocument document)
    {
        this.document = document;
        Title = "Exportar áudio";
        Width = 480;
        MaxHeight = SystemParameters.WorkArea.Height * .9;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var panel = new StackPanel { Margin = new(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = document.Title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = $"{document.Options.Voice?.Name ?? "Voz padrão do Windows"} · MP3 mono · 96 kbps" +
                (document.Estimate == null ? "" : $"\nDuração aproximada {document.Estimate}"),
            Margin = new(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(status);
        panel.Children.Add(progress);
        System.Windows.Automation.AutomationProperties.SetName(progress, "Progresso da exportação");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        actions.Children.Add(cancel);
        actions.Children.Add(export);
        panel.Children.Add(actions);
        export.Click += ExportAsync;
        cancel.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Closing += (_, e) =>
        {
            if (operation == null) return;
            e.Cancel = true;
            operation.Cancel();
            status.Text = "Cancelando…";
            cancel.IsEnabled = false;
        };
    }

    private async void ExportAsync(object sender, RoutedEventArgs e)
    {
        if (operation != null) return;
        var picker = new SaveFileDialog
        {
            FileName = Mp3Export.SuggestedName(document.Title), Filter = "Áudio MP3 (*.mp3)|*.mp3",
            DefaultExt = ".mp3", AddExtension = true, OverwritePrompt = false, Title = "Salvar áudio como"
        };
        if (picker.ShowDialog(this) != true) return;

        using var request = new CancellationTokenSource();
        operation = request;
        export.IsEnabled = false;
        progress.Visibility = Visibility.Visible;
        progress.IsIndeterminate = true;
        status.Text = "Preparando a narração…";
        var feedback = new Progress<int>(percent =>
        {
            if (operation != request || request.IsCancellationRequested) return;
            progress.IsIndeterminate = false;
            progress.Value = percent;
            status.Text = $"Exportando áudio · {percent}%";
        });
        try
        {
            await Mp3Export.ExportAsync(document.Text, document.Legacy, document.Options, picker.FileName, feedback, request.Token);
            progress.Value = 100;
            status.Text = "MP3 salvo.";
            cancel.Content = "Fechar";
        }
        catch (Exception) when (request.IsCancellationRequested)
        {
            progress.Visibility = Visibility.Collapsed;
            status.Text = "Exportação cancelada.";
            export.IsEnabled = true;
        }
        catch (Exception error)
        {
            progress.Visibility = Visibility.Collapsed;
            status.Text = error switch
            {
                PathTooLongException => "O caminho é longo demais. Escolha uma pasta ou um nome mais curto.",
                UnauthorizedAccessException => "A pasta não permite gravar com a conta atual.",
                IOException when File.Exists(picker.FileName) => "Já existe um arquivo nesse destino. Escolha outro nome.",
                IOException => "Falha ao gravar. Confira o espaço livre e a pasta de destino.",
                _ => "Não foi possível sintetizar ou codificar o áudio. Confira a voz escolhida e se o Windows tem os componentes de mídia instalados."
            };
            export.IsEnabled = true;
        }
        finally
        {
            operation = null;
            cancel.IsEnabled = true;
        }
    }
}
