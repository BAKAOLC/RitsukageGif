using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private async Task<byte[]> CaptureAnnotationSourceAsync(CancellationToken token)
        {
            var (plan, toneMap) = await Dispatcher.UIThread.InvokeAsync(() =>
                (PrepareStaticCapture(new(), token), Settings.CaptureToneMap));
            using var plannedSource = plan.SourceReference;
            using var source = await PrepareStaticSourceAsync(plan, false, token).ConfigureAwait(false);
            var session = source?.Session;
            return await Task.Run(() =>
            {
                var frame = CaptureStaticFrames(plan, false, toneMap, session, token).Sdr;
                var rgba = CapturedFramePixels.ToStraightBgra(frame, token);
                for (var y = 0; y < frame.Height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    var end = checked((y + 1) * frame.Width * 4);
                    for (var offset = y * frame.Width * 4; offset < end; offset += 4)
                        (rgba[offset], rgba[offset + 2]) = (rgba[offset + 2], rgba[offset]);
                }

                return PngImageCodec.Encode(new(frame.Width, frame.Height, 8, 6, rgba, []), token);
            }, token).ConfigureAwait(false);
        }
    }
}
