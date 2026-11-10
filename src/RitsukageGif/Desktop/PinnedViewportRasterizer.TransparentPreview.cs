using System.Buffers;

namespace RitsukageGif.Desktop
{
    internal static partial class PinnedViewportRasterizer
    {
        internal static PreviewFrame PreviewTransparent(CapturedFrame source, PinnedViewportSnapshot viewport,
            PinnedHdrPreviewBackground background, CancellationToken token = default)
        {
            var width = viewport.TargetSize.Width;
            var height = viewport.TargetSize.Height;
            var output = ArrayPool<byte>.Shared.Rent(checked(width * height * 8));
            try
            {
                RenderClamped(new PinnedViewportPreviewSource(source),
                    new PinnedViewportTransparentPreviewTarget(output, width, viewport.SourceToTarget, background,
                        viewport.Clip),
                    viewport.SourceToTarget, width, height, 1, viewport.SmoothScaling, token);
                return new([], width, height, 0, output,
                    source.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits,
                    CaptureAlphaMode.Premultiplied);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(output);
                throw;
            }
        }
    }
}
