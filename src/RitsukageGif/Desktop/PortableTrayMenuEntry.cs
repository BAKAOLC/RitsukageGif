using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record PortableTrayMenuEntry(
        int Id,
        int Parent,
        bool Enabled,
        int Toggle,
        bool Checked,
        bool Separator,
        string Label,
        KeyGesture? Gesture,
        byte[]? IconPng);
}
