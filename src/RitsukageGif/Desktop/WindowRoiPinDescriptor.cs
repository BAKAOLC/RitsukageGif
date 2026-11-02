namespace RitsukageGif.Desktop
{
    internal sealed record WindowRoiPinDescriptor(
        long Handle,
        uint ProcessId,
        string Process,
        string Title,
        PixelRect RelativeRegion,
        bool Cursor = false,
        int QuarterTurns = 0,
        bool FlipHorizontal = false,
        bool FlipVertical = false,
        NativeCaptureAuthorization? NativeAuthorization = null)
    {
        internal bool IsValid => (NativeAuthorization is not null
                                     ? Handle == -1 && ProcessId == 0 &&
                                       Guid.TryParseExact(NativeAuthorization.AuthorizationId, "N", out _) &&
                                       NativeAuthorization is { SourceIdentity.Length: > 0, Generation: > 0 }
                                     : Handle != 0 && Handle != -1 && ProcessId != 0) &&
                                 RelativeRegion is { Width: > 0 and <= 32768, Height: > 0 and <= 32768 } &&
                                 (long)RelativeRegion.Width * RelativeRegion.Height <= 128L * 1024 * 1024 &&
                                 QuarterTurns is >= 0 and < 4;

        internal bool MatchesSource(WindowRoiPinDescriptor other)
        {
            return Handle == other.Handle && ProcessId == other.ProcessId &&
                   NativeAuthorization == other.NativeAuthorization &&
                   RelativeRegion == other.RelativeRegion && Cursor == other.Cursor;
        }

        internal WindowRoiPinDescriptor Transform(PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => this with
                {
                    QuarterTurns = (QuarterTurns + 1) % 4,
                    FlipHorizontal = FlipVertical,
                    FlipVertical = FlipHorizontal,
                },
                PinnedImageTransform.Rotate180 => this with { QuarterTurns = (QuarterTurns + 2) % 4 },
                PinnedImageTransform.FlipHorizontal => this with { FlipHorizontal = !FlipHorizontal },
                PinnedImageTransform.FlipVertical => this with { FlipVertical = !FlipVertical },
                PinnedImageTransform.Grayscale or PinnedImageTransform.Invert =>
                    throw new InvalidOperationException("Convert the live pin before applying pixel effects."),
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
        }
    }
}
