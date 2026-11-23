using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using IconPath = System.Windows.Shapes.Path;

namespace Leitor.Presentation;
public sealed class AppIcon : UserControl
{
    private readonly IconPath path;
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(AppIcon), new PropertyMetadata("Book", (d, _) => ((AppIcon)d).Update()));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public AppIcon()
    {
        Focusable = false; IsHitTestVisible = false; Width = 18; Height = 18;
        path = new IconPath { StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });
        Content = new Viewbox { Child = new Canvas { Width = 24, Height = 24, Children = { path } } }; Update();
    }
    private void Update()
    {
        if (path == null) return;
        path.SetResourceReference(IconPath.DataProperty, "Icon" + Kind);
        if (Kind == "LibraryFolder") path.SetBinding(Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
        else path.ClearValue(Shape.FillProperty);
    }
}
public sealed class ReadingLineHeight : IValueConverter
{
    public object Convert(object value, Type target, object parameter, System.Globalization.CultureInfo culture) => value is double size ? size * 1.65 : 33d;
    public object ConvertBack(object value, Type target, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}
