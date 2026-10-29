namespace RitsukageGif.Desktop
{
    internal sealed record MouseCommandBinding(MouseCommandDefinition Definition, InputMouseGesture Gesture)
    {
        internal MouseAction Action => Definition.Action;
    }
}
