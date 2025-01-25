using System;
using System.Text;
using System.Windows.Input;

namespace RitsukageGif.Structs
{
    public readonly record struct HotKey(Key Key, ModifierKeys ModifierKeys)
    {
        public override string ToString()
        {
            var sb = new StringBuilder();
            if (ModifierKeys.HasFlag(ModifierKeys.Control)) sb.Append("Control+");
            if (ModifierKeys.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (ModifierKeys.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (ModifierKeys.HasFlag(ModifierKeys.Windows)) sb.Append("Windows+");
            sb.Append(Key.ToString());
            return sb.ToString();
        }

        public static HotKey Parse(string str)
        {
            var key = Key.None;
            var modifier = ModifierKeys.None;
            var keyConverter = new KeyConverter();
            foreach (var s in str.Split('+'))
                switch (s.ToLower().Trim())
                {
                    case "ctrl":
                    case "control":
                        modifier |= ModifierKeys.Control;
                        break;
                    case "alt":
                        modifier |= ModifierKeys.Alt;
                        break;
                    case "shift":
                        modifier |= ModifierKeys.Shift;
                        break;
                    case "win":
                    case "windows":
                        modifier |= ModifierKeys.Windows;
                        break;
                    default:
                    {
                        if (key == Key.None)
                        {
                            if (keyConverter.ConvertFromString(s) is Key k)
                                key = k;
                            else
                                throw new ArgumentException($"Invalid key string part: {s}", nameof(str));
                        }
                        else
                        {
                            throw new ArgumentException($"Invalid key string part: {s}", nameof(str));
                        }

                        break;
                    }
                }

            return new(key, modifier);
        }

        public static bool TryParse(string str, out HotKey hotKey)
        {
            try
            {
                hotKey = Parse(str);
                return true;
            }
            catch
            {
                hotKey = default;
                return false;
            }
        }
    }
}