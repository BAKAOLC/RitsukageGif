using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static class TrayGestureEncoding
    {
        internal static uint MacModifiers(KeyGesture? gesture)
        {
            var modifiers = gesture?.KeyModifiers ?? KeyModifiers.None;
            return (modifiers.HasFlag(KeyModifiers.Shift) ? 1u : 0) |
                   (modifiers.HasFlag(KeyModifiers.Control) ? 2u : 0) |
                   (modifiers.HasFlag(KeyModifiers.Alt) ? 4u : 0) |
                   (modifiers.HasFlag(KeyModifiers.Meta) ? 8u : 0);
        }

        internal static string MacKey(KeyGesture? gesture)
        {
            if (gesture is null) return "";
            var key = gesture.Key;
            return key switch
            {
                >= Key.A and <= Key.Z => ((char)('a' + key - Key.A)).ToString(),
                >= Key.D0 and <= Key.D9 => ((char)('0' + key - Key.D0)).ToString(),
                >= Key.F1 and <= Key.F24 => ((char)(0xf704 + key - Key.F1)).ToString(),
                >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + key - Key.NumPad0)).ToString(),
                Key.Space => " ", Key.Enter => "\r", Key.Tab => "\t", Key.Escape => "\e",
                Key.Back => "\b", Key.Delete => "\u007f", Key.Up => "\uf700", Key.Down => "\uf701",
                Key.Left => "\uf702", Key.Right => "\uf703", Key.Home => "\uf729", Key.End => "\uf72b",
                Key.PageUp => "\uf72c", Key.PageDown => "\uf72d", Key.Insert => "\uf727",
                Key.OemPlus or Key.Add => "+", Key.OemMinus or Key.Subtract => "-",
                Key.OemComma => ",", Key.OemPeriod or Key.Decimal => ".", Key.OemQuestion or Key.Divide => "/",
                Key.Multiply => "*", Key.OemSemicolon => ";", Key.OemQuotes => "'", Key.OemTilde => "`",
                Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\",
                _ => "",
            };
        }

        internal static string LinuxKeys(KeyGesture? gesture)
        {
            if (gesture is null) return "";
            var keys = new List<string>();
            var modifiers = gesture.KeyModifiers;
            if (modifiers.HasFlag(KeyModifiers.Control)) keys.Add("Control");
            if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
            if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
            if (modifiers.HasFlag(KeyModifiers.Meta)) keys.Add("Super");
            keys.Add(gesture.Key switch
            {
                Key.Enter => "Return", Key.Back => "BackSpace", Key.PageUp => "Page_Up", Key.PageDown => "Page_Down",
                Key.OemPlus => "plus", Key.OemMinus => "minus", Key.OemComma => "comma", Key.OemPeriod => "period",
                Key.OemQuestion => "slash", Key.OemSemicolon => "semicolon", Key.OemQuotes => "apostrophe",
                Key.OemTilde => "grave", Key.OemOpenBrackets => "bracketleft", Key.OemCloseBrackets => "bracketright",
                Key.OemPipe => "backslash", Key.Space => "space",
                >= Key.D0 and <= Key.D9 => ((char)('0' + gesture.Key - Key.D0)).ToString(),
                _ => gesture.Key.ToString(),
            });
            return string.Join('\t', keys);
        }
    }
}
