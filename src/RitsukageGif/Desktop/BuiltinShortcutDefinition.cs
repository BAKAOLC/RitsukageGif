using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed record BuiltinShortcutDefinition(
        string Id,
        string ContextKey,
        string DescriptionKey,
        string DefaultGesture,
        string Action,
        string? SelectionId = null,
        Key DirectionKey = Key.None);
}
