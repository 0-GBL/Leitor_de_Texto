using System.Windows;

namespace Leitor;
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) => { MessageBox.Show("O Leitor encontrou um problema: " + e.Exception.Message, "Leitor", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
    }
}
