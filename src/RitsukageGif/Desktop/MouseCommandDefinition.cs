namespace RitsukageGif.Desktop
{
    internal sealed record MouseCommandDefinition(
        string Id,
        MouseGestureContext Context,
        MouseAction Action,
        string DescriptionKey,
        MouseGestureKind AllowedKinds,
        IReadOnlyList<string> DefaultGestures);
}
