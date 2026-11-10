namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private async Task CreateCutOutPinAsync(ScreenshotResult output, PixelPoint position, CancellationToken token)
        {
            var png = output.Hdr is { } hdr
                ? await HdrImageService.EncodePngAsync(hdr, token)
                : output.Png;
            var content = await Task.Run(() => PinnedImageContent.Decode(png, token), token);
            token.ThrowIfCancellationRequested();
            Pin(content, position, Preferences.ActiveGroup, output.CaptureScaling);
        }
    }
}
