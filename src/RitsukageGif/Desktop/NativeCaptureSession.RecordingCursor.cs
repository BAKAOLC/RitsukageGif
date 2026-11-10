namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class NativeCaptureSession
    {
        internal static string? RecordingCursorUnavailableReason
        {
            get
            {
                lock (SharedLock)
                {
                    if (_shared is not { } session) return null;
                    return session.CursorUnavailableReason;
                }
            }
        }

        internal string? CursorUnavailableReason
        {
            get
            {
                lock (_gate)
                    return _capabilities is { SeparateCursor: false } capabilities
                        ? capabilities.CursorMode switch
                        {
                            2 =>
                                "The portal embeds the cursor in the captured image; an editable cursor track is unavailable.",
                            1 => "The portal selected hidden-cursor mode; independent cursor metadata is unavailable.",
                            _ => "The authorized capture source does not provide independent cursor metadata.",
                        }
                        : null;
            }
        }

        internal NativeCaptureCursorFrame? ReadCursor()
        {
            lock (_gate)
            {
                if (_disposed) throw new CaptureSourceLostException();
                return _pointer is { } pointer
                    ? new(pointer, _cursorPixels is { } pixels ? (byte[])pixels.Clone() : null)
                    : null;
            }
        }
    }
}
