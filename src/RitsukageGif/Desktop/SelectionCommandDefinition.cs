using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record SelectionCommandDefinition(
        SelectionCommand Command,
        Key Key,
        KeyModifiers Modifiers,
        string DescriptionKey,
        bool Repeat = false,
        ScreenshotTool? Tool = null,
        Key DirectionKey = Key.None)
    {
        internal string Id => Command == SelectionCommand.SelectTool
            ? "tool." + Tool!.Value.ToString().ToLowerInvariant()
            : Command.ToString().ToLowerInvariant() + (DirectionKey == Key.None
                ? string.Empty
                : "." + DirectionKey.ToString().ToLowerInvariant());

        internal string Gesture =>
            Key == Key.None ? string.Empty : KeyGestureDisplay.Format(new KeyGesture(Key, Modifiers));
    }
}
