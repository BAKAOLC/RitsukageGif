using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static class KeyGestureDisplay
    {
        internal static string Format(KeyGesture gesture)
        {
            var text = gesture.ToString();
            var key = gesture.Key switch
            {
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.LeftShift or Key.RightShift => "Shift",
                Key.OemTilde => "`",
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe => "\\",
                >= Key.D0 and <= Key.D9 => ((int)gesture.Key - (int)Key.D0).ToString(),
                _ => null,
            };
            return key is null ? text : text[..(text.LastIndexOf('+') + 1)] + key;
        }

        internal static string Format(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            try
            {
                return Format(Parse(text));
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            {
                return text;
            }
        }

        internal static KeyGesture Parse(string text)
        {
            var normalized = text.Trim();
            if (normalized.Equals("Shift", StringComparison.OrdinalIgnoreCase)) return new(Key.LeftShift);
            if (normalized.EndsWith('+')) return KeyGesture.Parse(normalized[..^1] + "OemPlus");
            var offset = normalized.LastIndexOf('+') + 1;
            var key = normalized[offset..].Trim();
            key = key switch
            {
                [>= '0' and <= '9'] => "D" + key,
                "`" => "OemTilde",
                "-" => "OemMinus",
                "," => "OemComma",
                "." => "OemPeriod",
                "/" => "OemQuestion",
                ";" => "OemSemicolon",
                "'" => "OemQuotes",
                "[" => "OemOpenBrackets",
                "]" => "OemCloseBrackets",
                "\\" => "OemPipe",
                _ when key.Equals("Backspace", StringComparison.OrdinalIgnoreCase) => "Back",
                _ => key,
            };
            return KeyGesture.Parse(normalized[..offset] + key);
        }
    }
}
