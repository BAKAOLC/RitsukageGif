using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static class BuiltinKeys
    {
        private static bool MixingEnabled { get; set; }

        internal static void Configure(bool enabled)
        {
            MixingEnabled = enabled;
        }

        internal static Key Normalize(Key key, bool mixing)
        {
            if (!mixing) return key;
            return key switch
            {
                >= Key.NumPad0 and <= Key.NumPad9 => key - Key.NumPad0 + Key.D0,
                Key.Add => Key.OemPlus,
                Key.Subtract => Key.OemMinus,
                Key.Divide => Key.OemQuestion,
                Key.Decimal => Key.OemPeriod,
                _ => key,
            };
        }

        internal static bool Matches(Key left, Key right)
        {
            return Normalize(left, MixingEnabled) == Normalize(right, MixingEnabled);
        }

        internal static IEnumerable<KeyGesture> NativeGestures(KeyGesture gesture)
        {
            yield return gesture;
            if (!MixingEnabled) yield break;
            var normal = Normalize(gesture.Key, true);
            var alternate = normal == gesture.Key
                ? normal switch
                {
                    >= Key.D0 and <= Key.D9 => normal - Key.D0 + Key.NumPad0,
                    Key.OemPlus => Key.Add,
                    Key.OemMinus => Key.Subtract,
                    Key.OemQuestion => Key.Divide,
                    Key.OemPeriod => Key.Decimal,
                    _ => normal,
                }
                : normal;
            if (alternate != gesture.Key) yield return new(alternate, gesture.KeyModifiers);
        }
    }
}
