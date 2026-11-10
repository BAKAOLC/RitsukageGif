using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal readonly record struct RecordingInputEvent(
        long Timestamp,
        RecordingInputEventKind Kind,
        Key Key = Key.None,
        KeyModifiers Modifiers = KeyModifiers.None,
        int Button = 0,
        double X = 0,
        double Y = 0,
        int WheelDelta = 0,
        uint NativeCode = 0,
        uint NativeScanCode = 0);
}
