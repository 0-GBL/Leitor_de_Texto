using System.Windows;
using System.Windows.Controls;
using Leitor.ViewModels;

namespace Leitor;
public partial class SettingsWindow : Window
{
    private bool ready;
    private MainViewModel Model => (MainViewModel)DataContext;
    public SettingsWindow(MainViewModel model) { InitializeComponent(); DataContext = model; Loaded += (_, _) => ready = true; }
    private void PreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (sender is Slider slider) slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();
        if (sender is CheckBox check) check.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
        Model.SavePreferences();
    }
    private void AppLockChanged(object sender, RoutedEventArgs e)
    {
        ((CheckBox)sender).GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateSource();
        if (!Model.Vault.Configured) { Model.Settings.AppLock = false; Model.Status = "Configure um PIN antes de ativar o bloqueio."; }
        else if (!SecurityDialog.Verify(Model.Vault, "Alterar bloqueio do aplicativo")) Model.Settings.AppLock = !Model.Settings.AppLock;
        Model.SavePreferences();
    }
    private void Done(object sender, RoutedEventArgs e) => Close();
}
