using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record BuiltinShortcutBinding(string ContextKey, string Action, KeyGesture Gesture);
}
