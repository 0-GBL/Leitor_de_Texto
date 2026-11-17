using System.Windows;
using System.Windows.Controls;
using Leitor.Services;

namespace Leitor;

public sealed class SecurityDialog : Window
{
    private readonly PasswordBox entry = new() { Margin = new(0, 12, 0, 12), Height = 36, FontSize = 19 };
    private readonly PasswordBox confirm = new() { Margin = new(0, 8, 0, 12), Height = 36, FontSize = 19 };
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private static int failures;
    private static DateTime nextAttempt;
    public string Pin => entry.Password;
    private SecurityDialog(string title, bool create, Vault? vault)
    {
        Title = title; Width = 410; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "SurfaceBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow;
        var panel = new StackPanel { Margin = new(28) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = create ? "Use pelo menos 6 dígitos. Não há recuperação do PIN.\nGuarde uma cópia segura dos textos importantes." : "Informe seu PIN para continuar.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(entry, "PIN"); panel.Children.Add(entry);
        if (create) { panel.Children.Add(new TextBlock { Text = "Confirme o PIN" }); System.Windows.Automation.AutomationProperties.SetName(confirm, "Confirmar PIN"); panel.Children.Add(confirm); }
        panel.Children.Add(feedback);
        var button = new Button { Content = create ? "Ativar proteção" : "Desbloquear", IsDefault = true, Padding = new(12) };
        button.Click += async (_, _) =>
        {
            if (DateTime.UtcNow < nextAttempt) { feedback.Text = $"Aguarde {(nextAttempt - DateTime.UtcNow).TotalSeconds:0} segundos."; return; }
            if (create)
            {
                if (Pin.Length < 6 || !Pin.All(char.IsAsciiDigit) || Pin != confirm.Password) { feedback.Text = "Use ao menos 6 dígitos e confirme o mesmo PIN."; return; }
                DialogResult = true; return;
            }
            button.IsEnabled = false; string pin = Pin;
            try
            {
                if (await Task.Run(() => vault!.Unlock(pin))) { failures = 0; DialogResult = true; }
                else { failures++; nextAttempt = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, failures))); feedback.Text = "PIN incorreto. Aguarde antes de tentar novamente."; entry.Clear(); }
            }
            catch (Exception e) { feedback.Text = "Não foi possível abrir a proteção: " + e.Message; }
            finally { button.IsEnabled = true; }
        };
        panel.Children.Add(button); Loaded += (_, _) => entry.Focus();
    }
    public static bool Verify(Vault vault, string title) => new SecurityDialog(title, false, vault).ShowDialog() == true;
    public static string? CreatePin() { var window = new SecurityDialog("Criar PIN", true, null); return window.ShowDialog() == true ? window.Pin : null; }
}
