using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record SelectionShortcutDefinition(
        string Id,
        string DescriptionKey,
        string CategoryKey,
        IReadOnlyList<string> DefaultGestures,
        Key DirectionKey);
}
