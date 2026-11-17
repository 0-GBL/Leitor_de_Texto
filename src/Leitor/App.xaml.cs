using System.Windows;

namespace Leitor;
public partial class App : Application
{
    public App() : this(null) { }
    public App(Action<Exception>? errorReporter)
    {
        DispatcherUnhandledException += (_, e) =>
        {
            if (errorReporter != null) errorReporter(e.Exception);
            else MessageBox.Show("Não foi possível concluir a operação. Seus arquivos foram preservados. Consulte o relatório de validação se o problema persistir.", "Leitor", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
