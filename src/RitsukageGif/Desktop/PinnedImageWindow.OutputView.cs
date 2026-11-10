using Avalonia;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool CanCaptureOriginalImage =>
            _isThumbnail || !_viewTransform.IsIdentity || CurrentPinZoom is not 1 || Opacity < 1;

        private PinnedViewportSnapshot SnapshotOutputView()
        {
            var useView = _isThumbnail || !_viewTransform.IsIdentity || CurrentPinZoom is not 1;
            if (!useView)
                return new(Matrix.Identity, default, false, Opacity, CurrentPreferences.SmoothScaling, RenderScaling);
            return SnapshotImageViewport();
        }

        private PinnedViewportSnapshot SnapshotImageViewport()
        {
            UpdateLayout();
            var region = DisplayRegion;
            var imageWidth = _image.Bounds.Width > 0 ? _image.Bounds.Width : _image.Width;
            var imageHeight = _image.Bounds.Height > 0 ? _image.Bounds.Height : _image.Height;
            var imageToView = _image.TransformToVisual(_viewport) ??
                              _viewTransform.MatrixFor(new(imageWidth, imageHeight)) *
                              Matrix.CreateTranslation(-_viewport.Offset.X, -_viewport.Offset.Y);
            var matrix = Matrix.CreateTranslation(-region.X, -region.Y) *
                         Matrix.CreateScale(imageWidth / region.Width, imageHeight / region.Height) *
                         imageToView * Matrix.CreateScale(RenderScaling, RenderScaling);
            var viewport = _viewport.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                var extent = _viewTransform.ViewSize(new(imageWidth, imageHeight));
                viewport = new(Math.Min(extent.Width, Width), Math.Min(extent.Height, Height));
            }

            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(viewport.Width * RenderScaling)),
                Math.Max(1, (int)Math.Ceiling(viewport.Height * RenderScaling)));
            var targetClip = new Rect(0, 0, size.Width, size.Height);
            if (_sceneTransform.ClipToBounds && _sceneTransform.TransformToVisual(_viewport) is { } sceneToView)
                targetClip = targetClip.Intersect(new Rect(_sceneTransform.Bounds.Size)
                    .TransformToAABB(sceneToView * Matrix.CreateScale(RenderScaling, RenderScaling)));
            return new(matrix, size, true, Opacity, CurrentPreferences.SmoothScaling, RenderScaling,
                new(new(region.X, region.Y, region.Width, region.Height), targetClip));
        }

        private async Task CopyOriginalImageAsync()
        {
            var output = await CaptureSceneOutputAsync(OutputColorModeProvider(), _lifetime.Token);
            await CopyImageAsync(output);
            await SaveAutomaticallyAsync(ScreenshotAutoSaveTriggers.Copy, output);
        }

        private async Task SaveOriginalImageAsync()
        {
            await SaveOutputAsync(await CaptureSceneOutputAsync(OutputColorModeProvider(), _lifetime.Token));
        }
    }
}
