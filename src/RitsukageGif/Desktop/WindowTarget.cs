namespace RitsukageGif.Desktop
{
    internal sealed record WindowTarget(nint Handle, string Process, string Title, PixelRect Bounds, PixelRect Client)
    {
        internal uint ProcessId { get; init; }
        internal string? NativeSourceIdentity { get; init; }
        internal NativeCaptureAuthorization? NativeAuthorization { get; init; }
    }
}
