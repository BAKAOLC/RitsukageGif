using Avalonia;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private async Task ConvertLiveToImageAsync(PinnedImageWindow source)
        {
            var token = ((IPinnedWindow)source).OutputLifetime;
            var opacity = source.Opacity;
            var zoom = source.CurrentPinZoom;
            var center = source.Presentation.ScreenCenter;
            var contentOrigin = source.PointToScreen(new(PinnedWindowPresentation.ShadeInset,
                PinnedWindowPresentation.ShadeInset));
            var output = await source.CaptureLiveImageConversionAsync(token);
            var png = output.Hdr is { } sourceHdr
                ? await HdrImageService.EncodePngAsync(sourceHdr, token)
                : output.Png;
            var content = await Task.Run(() =>
            {
                var decoded = PinnedImageContent.Decode(output.Png, token);
                return output.Hdr is { } hdr ? PinnedImageContent.FromHdr(png, decoded, hdr) : decoded;
            }, token);
            token.ThrowIfCancellationRequested();
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed || !AllWindows.Contains(source)) return;
            if (Pin(content, new(contentOrigin.X, contentOrigin.Y), "", output.CaptureScaling,
                    prepareView: prepared => prepared.PrepareConvertedView(zoom, opacity)) is not { } created) return;
            created.Presentation.SetScreenCenter(center);
            CloseSelection(source, false);
            ScheduleSave();
        }
    }
}
