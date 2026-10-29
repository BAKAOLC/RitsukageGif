using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal readonly record struct InputMouseGesture(MouseGestureKind Kind, KeyModifiers Modifiers)
    {
        internal static bool TryParse(string? text, out InputMouseGesture gesture)
        {
            gesture = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split('+', StringSplitOptions.TrimEntries);
            if (!Enum.TryParse<MouseGestureKind>(parts[^1], true, out var kind) ||
                kind == MouseGestureKind.None || ((int)kind & ((int)kind - 1)) != 0 ||
                !Enum.IsDefined(kind) ||
                !parts[^1].Equals(kind.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
            var modifiers = KeyModifiers.None;
            foreach (var part in parts.AsSpan(0, parts.Length - 1))
            {
                var modifier = part.ToLowerInvariant() switch
                {
                    "ctrl" or "control" => KeyModifiers.Control,
                    "shift" => KeyModifiers.Shift,
                    "alt" => KeyModifiers.Alt,
                    "meta" or "win" or "cmd" => KeyModifiers.Meta,
                    _ => KeyModifiers.None,
                };
                if (modifier == KeyModifiers.None || (modifiers & modifier) != 0) return false;
                modifiers |= modifier;
            }

            gesture = new(kind, modifiers);
            return true;
        }

        public override string ToString()
        {
            return (Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : string.Empty) +
                   (Modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : string.Empty) +
                   (Modifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : string.Empty) +
                   (Modifiers.HasFlag(KeyModifiers.Meta) ? "Meta+" : string.Empty) + Kind;
        }
    }
}
