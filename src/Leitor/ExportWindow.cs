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
    private readonly ComboBox voices = new() { DisplayMemberPath = "Name" };
    private readonly TextBox diagnostic = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap };
    private readonly Expander details = new() { Header = "Detalhes técnicos", Visibility = Visibility.Collapsed };
    private readonly Button manage = new() { Content = "Gerenciar vozes" };
    private CancellationTokenSource? operation;

    public ExportWindow(ExportDocument document, IEnumerable<Voice>? available = null, Func<Window, Task>? manageVoices = null)
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
            Text = "MP3 mono · 96 kbps" +
                (document.Estimate == null ? "" : $"\nDuração aproximada {document.Estimate}"),
            Margin = new(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new Label { Content = "_Voz da exportação", Target = voices });
        panel.Children.Add(voices);
        SetVoices();
        if (manageVoices != null)
        {
            manage.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(manage);
            manage.Click += async (_, _) =>
            {
                manage.IsEnabled = export.IsEnabled = false;
                try { await manageVoices(this); SetVoices(); }
                finally { manage.IsEnabled = export.IsEnabled = true; }
            };
        }
        panel.Children.Add(status);
        panel.Children.Add(progress);
        details.Content = diagnostic;
        panel.Children.Add(details);
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

        void SetVoices()
        {
            var current = voices.SelectedItem as Voice ?? document.Options.Voice;
            var items = available?.ToList() ?? [];
            if (current != null && !items.Any(item => item.Id == current.Id)) items.Insert(0, current);
            if (current == null) items.Insert(0, new Voice("", "Voz padrão do Windows", "Default", ""));
            voices.ItemsSource = items;
            voices.SelectedItem = items.FirstOrDefault(item => item.Id == (current?.Id ?? ""));
        }
    }

    private async void ExportAsync(object sender, RoutedEventArgs e)
    {
        if (operation != null) return;
        var selected = voices.SelectedItem as Voice;
        var options = document.Options with { Voice = selected is { Engine: "Default" } ? null : selected };
        details.Visibility = Visibility.Collapsed;
        try { Mp3Export.ValidateVoice(options.Voice); }
        catch (Mp3ExportException error) { ShowError(error.Message, error.Diagnostic); return; }
        var picker = new SaveFileDialog
        {
            FileName = Mp3Export.SuggestedName(document.Title), Filter = "Áudio MP3 (*.mp3)|*.mp3",
            DefaultExt = ".mp3", AddExtension = true, OverwritePrompt = false, Title = "Salvar áudio como"
        };
        if (picker.ShowDialog(this) != true) return;

        using var request = new CancellationTokenSource();
        operation = request;
        export.IsEnabled = voices.IsEnabled = manage.IsEnabled = false;
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
            await Mp3Export.ExportAsync(document.Text, document.Legacy, options, picker.FileName, feedback, request.Token);
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
            string message = error switch
            {
                Mp3ExportException stage => stage.Message,
                _ when Mp3Export.IsStorageError(error) => Mp3Export.StorageMessage(error),
                PathTooLongException => "O caminho é longo demais. Escolha uma pasta ou um nome mais curto.",
                UnauthorizedAccessException => "A pasta não permite gravar com a conta atual.",
                IOException when File.Exists(picker.FileName) => "Já existe um arquivo nesse destino. Escolha outro nome.",
                IOException => "Falha ao gravar. Confira o espaço livre e a pasta de destino.",
                _ => "Não foi possível preparar ou finalizar a exportação. Consulte os detalhes técnicos."
            };
            ShowError(message, error is Mp3ExportException failure ? failure.Diagnostic : $"Destino/preparação · HRESULT 0x{error.HResult:X8}");
            export.IsEnabled = true;
        }
        finally
        {
            operation = null;
            cancel.IsEnabled = true;
            voices.IsEnabled = manage.IsEnabled = true;
        }
    }

    private void ShowError(string message, string code)
    {
        status.Text = message;
        diagnostic.Text = code;
        details.Visibility = Visibility.Visible;
        System.Diagnostics.Trace.TraceWarning("Exportação MP3: {0}", code);
    }
}
