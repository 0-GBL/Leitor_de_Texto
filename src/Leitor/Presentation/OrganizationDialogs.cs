using System.Windows;
using System.Windows.Controls;
using Leitor.Services;

namespace Leitor.Presentation;
public static class OrganizationDialogs
{
    internal static Window Window(string title,double height=260)
    {
        var owner=Application.Current.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive)??Application.Current.Windows.OfType<Leitor.MainWindow>().FirstOrDefault(x=>x.IsVisible);
        var w=new Window{Title=title,Width=520,Height=height,MinWidth=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=owner,ShowActivated=owner?.IsActive??true,FontFamily=new("Segoe UI"),FontSize=14};if(owner!=null&&!owner.IsActive){w.WindowStartupLocation=WindowStartupLocation.Manual;w.Left=owner.Left;w.Top=owner.Top;}w.SetResourceReference(System.Windows.Window.BackgroundProperty,"BackgroundBrush");w.SetResourceReference(System.Windows.Window.ForegroundProperty,"TextBrush");w.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;w.Close();}};return w;
    }
    public static string? Name(string title, string value, string? location = null)
    {
        var window=Window(title);var panel=new StackPanel{Margin=new(24)};window.Content=panel;
        window.SizeToContent = SizeToContent.Height;
        panel.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeights.SemiBold});
        var input = new TextBox { Text = value, Margin = new(0, 5, 0, 14) };
        panel.Children.Add(new Label { Content = "_Nome", Target = input, Padding = new(0, 12, 0, 0) });
        panel.Children.Add(input);
        if (location != null)
        {
            var destination = new TextBlock { Text = $"Localização: {location}", TextWrapping = TextWrapping.Wrap, MaxHeight = 105, Margin = new(0, 0, 0, 14), ToolTip = location };
            destination.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            panel.Children.Add(destination);
        }
        string? result=null;var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="Cancelar",IsCancel=true};var ok=new Button{Content="Salvar",IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(ok);panel.Children.Add(buttons);cancel.Click+=(_,_)=>window.Close();ok.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(input.Text))return;result=input.Text.Trim();window.Close();};window.Loaded+=(_,_)=>{input.Focus();input.SelectAll();};window.ShowDialog();return result;
    }
    private sealed record Choice(string? Id,string Label){public override string ToString()=>Label;}
    public static (bool Accepted,string? Id) Destination(IReadOnlyList<LibraryFolder> folders,string? movingFolder,bool copy=false)
    {
        var forbidden=movingFolder==null?[]:LibraryStore.Descendants(folders,movingFolder);var map=folders.ToDictionary(f=>f.Id);
        var choices = new List<Choice> { new(null, "Minha biblioteca") };
        foreach (var group in folders.Where(f => !forbidden.Contains(f.Id)).GroupBy(f => LibraryStore.FolderPath(map, f.Id)))
            choices.AddRange(group.Select((folder, index) => new Choice(folder.Id, group.Count() == 1 ? group.Key : $"{group.Key} ({index + 1})")));
        var window=Window(copy?"Copiar para pasta":"Mover para pasta",520);var panel=new DockPanel{Margin=new(24)};window.Content=panel;var search=new TextBox{Margin=new(0,0,0,12),ToolTip="Pesquisar pasta por nome"};DockPanel.SetDock(search,Dock.Top);panel.Children.Add(search);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new(0,14,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);var cancel=new Button{Content="Cancelar",IsCancel=true};var move=new Button{Content=copy?"Copiar":"Mover",IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(move);
        var list=new ListBox{ItemsSource=choices,SelectedIndex=0};panel.Children.Add(list);search.TextChanged+=(_,_)=>list.ItemsSource=choices.Where(c=>c.Label.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();bool accepted=false;string? id=null;cancel.Click+=(_,_)=>window.Close();move.Click+=(_,_)=>{if(list.SelectedItem is Choice choice){accepted=true;id=choice.Id;window.Close();}};window.ShowDialog();return(accepted,id);
    }
    public static int DeleteFolder(string name,FolderImpact impact)
    {
        var window=Window("Excluir pasta",360);var panel=new StackPanel{Margin=new(24)};window.Content=panel;panel.Children.Add(new TextBlock{Text=$"Excluir “{name}”?",FontSize=20,TextWrapping=TextWrapping.Wrap});panel.Children.Add(new TextBlock{Text=$"{impact.Documents} documentos e {impact.Subfolders} subpastas afetados. Escolha como tratar o conteúdo.",TextWrapping=TextWrapping.Wrap,Margin=new(0,16,0,20)});
        int result=0;foreach(var choice in new[]{(1,"Somente a pasta · transferir conteúdo para a superior"),(2,"Pasta e todo o conteúdo…"),(0,"Cancelar")}){var button=new Button{Content=choice.Item2,Margin=new(0,0,0,10)};panel.Children.Add(button);button.Click+=(_,_)=>{result=choice.Item1;window.Close();};}window.ShowDialog();return result;
    }
}
