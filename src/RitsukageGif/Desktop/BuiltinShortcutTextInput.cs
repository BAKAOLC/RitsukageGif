using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal static class BuiltinShortcutTextInput
    {
        internal static bool OwnsKey(KeyEventArgs args)
        {
            if (args.Source is not Control source) return false;
            var editor = source as TextBox ?? source.FindAncestorOfType<TextBox>();
            if (editor is null) return false;
            return editor.GetLogicalAncestors().OfType<PinnedWindowTitle>().Any() || IsEditingKey(args);
        }

        internal static bool IsEditingKey(KeyEventArgs args)
        {
            var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
            if (hotkeys is not null &&
                (hotkeys.Copy.Any(gesture => gesture.Matches(args)) ||
                 hotkeys.Cut.Any(gesture => gesture.Matches(args)) ||
                 hotkeys.Paste.Any(gesture => gesture.Matches(args)) ||
                 hotkeys.SelectAll.Any(gesture => gesture.Matches(args)) ||
                 hotkeys.Undo.Any(gesture => gesture.Matches(args)) ||
                 hotkeys.Redo.Any(gesture => gesture.Matches(args)))) return true;
            if (args.Key is Key.Enter or Key.Escape or Key.Back or Key.Delete or Key.Tab or
                Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                return true;
            var modifiers = args.KeyModifiers;
            if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 &&
                (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) !=
                (KeyModifiers.Control | KeyModifiers.Alt)) return false;
            return !string.IsNullOrEmpty(args.KeySymbol) || args.Key is >= Key.A and <= Key.Z or
                >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9 or Key.Space or
                Key.OemMinus or Key.OemPlus or Key.OemQuestion or Key.OemPeriod or Key.OemComma or
                Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemQuotes or Key.OemSemicolon or
                Key.OemTilde or Key.OemBackslash or Key.Add or Key.Subtract or Key.Multiply or Key.Divide
                or Key.Decimal;
        }
    }
}
