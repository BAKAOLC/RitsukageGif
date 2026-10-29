namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private async Task<(CapturedFrame? Frame, string? Reason)> ComposeSelectedHdrScreenshotAsync(
            SelectionResult result, ScreenshotAnnotationSession annotations, CancellationToken token)
        {
            var (hdr, hdrReason) = await Task.Run(() => ExportHdrScreenshot(result, token), token);
            CapturedFrame? hdrSamples = null;
            PixelRect? hdrSampleBounds = null;
            if (hdr is not null && annotations.HasMosaicAnnotations)
            {
                (hdrSamples, hdrSampleBounds, var sampleError) =
                    await Task.Run(() => ExportHdrSamples(result, token), token);
                if (hdrSamples is null)
                {
                    hdr = null;
                    hdrReason = sampleError;
                }
            }

            if (hdr is not null && !UsesLocalSource && result.Cursor && ScreenshotCursor is { } cursor &&
                !cursor.Bounds.Intersect(result.Region).IsEmpty)
            {
                var cursorPixels = await Task.Run(cursor.ReadTransparentPixels, token);
                if (cursorPixels is null)
                {
                    hdr = null;
                    hdrReason = UiText.Get("HdrScreenshotCursorUnsupported");
                }
                else
                {
                    hdr = await HdrAnnotationBlender.CompositeAsync(hdr, result.Region, cursorPixels,
                        cursor.Bounds, token);
                }
            }

            if (hdr is not null)
                hdr = await annotations.ComposeHdrAsync(result.Region, hdr, hdrSamples, hdrSampleBounds, token);
            return (hdr, hdrReason);
        }
    }
}
