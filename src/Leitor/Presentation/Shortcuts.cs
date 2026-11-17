using System.Windows.Input;

namespace Leitor.Presentation;
public enum ShortcutAction { New, Paste, Save, Play, Stop, Restart, Back, Forward, PreviousParagraph, NextParagraph, PreviousHeading, NextHeading, Theme, Search, Settings, Help, Jump }
public sealed record ShortcutRow(string Gesture, string Description);
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);
public static class Shortcuts
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } = [
        new("Gerenciamento de textos", [new("Ctrl + N", "Novo texto"), new("Ctrl + V", "Colar no campo atual; fora da edição, colar no editor"), new("Ctrl + S", "Salvar texto"), new("Ctrl + F", "Pesquisar na biblioteca")]),
        new("Reprodução de áudio", [new("Ctrl + Espaço", "Iniciar, pausar ou continuar"), new("Espaço", "Reproduzir fora da edição e de controles que usam Espaço"), new("Esc", "Interromper e preservar o progresso"), new("Ctrl + R", "Reiniciar leitura"), new("Ctrl + ← / →", "Voltar / avançar dez segundos fora da edição")]),
        new("Navegação", [new("Ctrl + ↑ / ↓", "Parágrafo anterior / próximo, fora da edição"), new("Ctrl + Shift + ↑ / ↓", "Título anterior / próximo, fora da edição"), new("Enter", "Ir à frase selecionada na área de leitura"), new("← / → na barra", "Prévia de cinco segundos; soltar confirma"), new("Page Up / Page Down na barra", "Avançar / retroceder trinta segundos"), new("Home / End na barra", "Início / final da leitura"), new("Esc durante o arraste", "Cancelar o destino sem alterar o áudio")]),
        new("Aparência", [new("Ctrl + T", "Alternar tema sem interromper a leitura")]),
        new("Configurações e ajuda", [new("Ctrl + vírgula", "Abrir configurações"), new("F1", "Abrir esta central"), new("Tab / Shift + Tab", "Navegar pelos controles"), new("Ctrl + Tab", "Sair do editor que aceita tabulação")])];
    public static ShortcutAction? Resolve(Key key, ModifierKeys modifiers, bool editing, bool nativeSpace = false, bool reader = false)
    {
        if (modifiers == ModifierKeys.None)
            return key switch { Key.F1 => ShortcutAction.Help, Key.Space when !editing && !nativeSpace => ShortcutAction.Play, Key.Escape => ShortcutAction.Stop, Key.Enter when reader && !editing => ShortcutAction.Jump, _ => null };
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && !editing)
            return key switch { Key.Up => ShortcutAction.PreviousHeading, Key.Down => ShortcutAction.NextHeading, _ => null };
        if (modifiers != ModifierKeys.Control) return null;
        return key switch
        {
            Key.N => ShortcutAction.New, Key.S => ShortcutAction.Save, Key.Space => ShortcutAction.Play, Key.R => ShortcutAction.Restart,
            Key.T => ShortcutAction.Theme, Key.F => ShortcutAction.Search, Key.OemComma => ShortcutAction.Settings,
            Key.V when !editing => ShortcutAction.Paste,
            Key.Left when !editing => ShortcutAction.Back, Key.Right when !editing => ShortcutAction.Forward,
            Key.Up when !editing => ShortcutAction.PreviousParagraph, Key.Down when !editing => ShortcutAction.NextParagraph, _ => null
        };
    }
}
