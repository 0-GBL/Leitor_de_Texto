using System.Windows;
using Leitor.Presentation;
namespace Leitor;
public partial class HelpWindow : Window
{
    public HelpWindow() { InitializeComponent(); MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24); Height = Math.Min(Height, MaxHeight); DataContext = Shortcuts.Groups; PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } }; }
    private void Done(object sender, RoutedEventArgs e) => Close();
}
