using System.Windows;
using System.Windows.Controls;
using Leitor.Services;

namespace Leitor;

public sealed class SecurityDialog : Window
{
    private readonly PasswordBox entry = new() { Margin = new(0, 12, 0, 12), Height = 36, FontSize = 19 };
    private readonly PasswordBox confirm = new() { Margin = new(0, 8, 0, 12), Height = 36, FontSize = 19 };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private string? acceptedPin;
    private bool verifying;
    public string Pin => entry.Password;
    private SecurityDialog(string title, bool create, Vault? vault)
    {
        Title = title; Width = 410; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        var panel = new StackPanel { Margin = new(28) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = create ? "Use pelo menos 6 dígitos. Não há recuperação do PIN.\nGuarde uma cópia segura dos textos importantes." : "Informe seu PIN para continuar.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(entry, "PIN"); panel.Children.Add(entry);
        if (create) { panel.Children.Add(new TextBlock { Text = "Confirme o PIN" }); System.Windows.Automation.AutomationProperties.SetName(confirm, "Confirmar PIN"); panel.Children.Add(confirm); }
        panel.Children.Add(feedback);
        var button = new Button { Content = create ? "Ativar proteção" : "Desbloquear", IsDefault = true, Padding = new(12) };
        button.Click += async (_, _) =>
        {
            if (!create && vault!.RemainingLockout > TimeSpan.Zero) { feedback.Text = $"Aguarde {Math.Ceiling(vault.RemainingLockout.TotalSeconds)} segundos."; return; }
            if (create)
            {
                try { Vault.ValidateNewPin(Pin); } catch (InvalidOperationException error) { feedback.Text = error.Message; return; }
                if (Pin != confirm.Password) { feedback.Text = "A confirmação deve ser igual ao novo PIN."; return; }
                acceptedPin = Pin; DialogResult = true; return;
            }
            button.IsEnabled = false; verifying = true; string pin = Pin;
            try
            {
                if (await Task.Run(() => vault!.Unlock(pin))) { verifying = false; DialogResult = true; }
                else { feedback.Text = "PIN não aceito. Aguarde antes de tentar novamente."; entry.Clear(); }
            }
            catch (Exception e) { feedback.Text = "Não foi possível abrir a proteção: " + e.Message; }
            finally { verifying = false; button.IsEnabled = true; }
        };
        panel.Children.Add(button); Loaded += (_, _) => entry.Focus();
        Closing += (_, e) => { if (verifying) e.Cancel = true; };
        Closed += (_, _) => { entry.Clear(); confirm.Clear(); };
    }
    public static bool Verify(Vault vault, string title) => new SecurityDialog(title, false, vault).ShowDialog() == true;
    public static string? CreatePin(string title = "Criar PIN") { var window = new SecurityDialog(title, true, null); return window.ShowDialog() == true ? window.acceptedPin : null; }
}
