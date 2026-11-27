using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Leitor.Core;
using Leitor.Services;

namespace Leitor.Presentation;

internal sealed class FolderAppearanceDialog
{
    public Window Window { get; }
    public FolderAppearance Appearance { get; private set; }

    private sealed record IconChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    public FolderAppearanceDialog(LibraryFolder folder, FolderVisual? parent, bool dark)
    {
        Appearance = folder.Appearance;
        Window = OrganizationDialogs.Window("Personalizar pasta");
        Window.SizeToContent = SizeToContent.Height;
        Window.ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new(24) };
        var background = new Border { Child = panel };
        background.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
        Window.Content = background;
        panel.Children.Add(new TextBlock { Text = Window.Title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        var previewIcon = new AppIcon { Width = 25, Height = 25 };
        var badge = new Border
        {
            Child = previewIcon, Width = 44, Height = 44, CornerRadius = new(10),
            Margin = new(0, 0, 12, 0)
        };
        var preview = new DockPanel();
        preview.Children.Add(badge);
        preview.Children.Add(new TextBlock
        {
            Text = folder.Name, ToolTip = folder.Name, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 62,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
        });
        var previewCard = new Border { Child = preview, Padding = new(12), Margin = new(0, 12, 0, 12) };
        previewCard.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        AutomationProperties.SetName(previewCard, "Pré-visualização da pasta");
        panel.Children.Add(previewCard);

        var choices = new List<IconChoice> { new(null, "Sugerido pelo nome") };
        choices.AddRange(FolderAppearanceCatalog.Icons.Select(icon => new IconChoice(icon.Id, icon.Name)));
        var icons = new ComboBox { ItemsSource = choices };
        icons.SelectedItem = choices.FirstOrDefault(choice => choice.Id == Appearance.Icon) ?? choices[0];
        var iconLabel = new Label { Content = "_Ícone", Target = icons, Padding = new(0, 0, 0, 4) };
        iconLabel.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        panel.Children.Add(iconLabel);
        panel.Children.Add(icons);

        panel.Children.Add(new TextBlock { Text = "Cor do ícone", FontWeight = FontWeights.SemiBold, Margin = new(0, 14, 0, 6) });
        var colors = new UniformGrid { Columns = 3, Margin = new(0, 0, 0, 8) };
        panel.Children.Add(colors);
        var colorButtons = new Dictionary<string, RadioButton>();
        foreach (var (id, label) in FolderAppearanceCatalog.Colors)
        {
            var swatch = new Border
            {
                Background = FolderPalette.Tint(new("LibraryFolder", id), dark),
                Width = 10, Height = 10, CornerRadius = new(5), Margin = new(0, 0, 6, 0)
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = label });
            var radio = new RadioButton { Content = content, GroupName = "FolderColor", Margin = new(0, 4, 0, 4) };
            radio.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            AutomationProperties.SetName(radio, label);
            colors.Children.Add(radio);
            colorButtons.Add(id, radio);
            radio.Checked += (_, _) => { Appearance = Appearance with { Color = id }; Preview(); };
        }

        var inherit = new RadioButton
        {
            Content = parent != null ? "Herdar a cor da pasta pai" : "Herdar quando houver uma pasta pai",
            GroupName = "FolderColor", Margin = new(0, 3, 0, 5)
        };
        panel.Children.Add(inherit);
        var suggested = new RadioButton { Content = "Usar a cor sugerida", GroupName = "FolderColor" };
        inherit.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        suggested.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        panel.Children.Add(suggested);
        inherit.Checked += (_, _) => { Appearance = Appearance with { Color = FolderAppearance.Inherit }; Preview(); };
        suggested.Checked += (_, _) => { Appearance = Appearance with { Color = null }; Preview(); };
        icons.SelectionChanged += (_, _) =>
        {
            if (icons.SelectedItem is IconChoice selected) Appearance = Appearance with { Icon = selected.Id };
            Preview();
        };

        var buttons = new DockPanel { Margin = new(0, 18, 0, 0), LastChildFill = false };
        var restore = new Button { Content = "Restaurar sugestões", Margin = new(0) };
        var save = new Button { Content = "Salvar", IsDefault = true, Margin = new(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new(0) };
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        buttons.Children.Add(restore);
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        restore.Click += (_, _) =>
        {
            Appearance = FolderAppearance.Suggested;
            icons.SelectedIndex = 0;
            suggested.IsChecked = true;
            Preview();
        };
        save.Click += (_, _) => Window.DialogResult = true;
        Window.Loaded += (_, _) => icons.Focus();

        if (Appearance.Color == FolderAppearance.Inherit) inherit.IsChecked = true;
        else if (Appearance.Color is string color && colorButtons.TryGetValue(color, out var selected)) selected.IsChecked = true;
        else suggested.IsChecked = true;
        Preview();

        void Preview()
        {
            var visual = FolderAppearanceCatalog.Resolve(folder.Name, Appearance, parent);
            previewIcon.Kind = visual.Icon;
            previewIcon.Foreground = FolderPalette.Tint(visual, dark);
            badge.Background = FolderPalette.Tint(visual, dark, badge: true);
        }
    }
}
