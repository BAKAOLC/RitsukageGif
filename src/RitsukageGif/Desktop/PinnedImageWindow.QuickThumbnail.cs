using Avalonia;
using Avalonia.Interactivity;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private Vector _thumbnailFullScrollPhysical;
        private Point? _thumbnailPointer;
        private bool _thumbnailManualRegion;
        private PixelSize? _quickThumbnailSourceSize;
        private double _thumbnailNormalZoom;
        private double _thumbnailSavedZoom;
        private EventHandler? _thumbnailLayoutCompletion;
        private (Avalonia.PixelPoint Origin, double Zoom, Vector Scroll)? _thumbnailLayoutBasis;

        private PixelSize QuickThumbnailStandardDip => new(
            CurrentPreferences.QuickThumbnailWidth ?? PinnedViewportGeometry.DefaultThumbnailSourceSize,
            CurrentPreferences.QuickThumbnailHeight ?? PinnedViewportGeometry.DefaultThumbnailSourceSize);

        private double PreferredQuickThumbnailZoom => Math.Max(
            _thumbnailSavedZoom > 0 ? _thumbnailSavedZoom : CurrentPinZoom,
            PinnedViewportGeometry.MinimumThumbnailScale(
                new((int)_bitmap.Size.Width, (int)_bitmap.Size.Height), QuickThumbnailStandardDip, RenderScaling));

        private void ConfigureQuickThumbnailPointer()
        {
            AddHandler(PointerMovedEvent, (_, args) => _thumbnailPointer = args.GetPosition(_viewport),
                RoutingStrategies.Tunnel);
        }

        private PixelRect InitialQuickThumbnailRegion()
        {
            PrepareThumbnailLayout();
            var pointer = PinnedGroupPointer.Read() is { } screenPoint
                ? PinnedWindowCoordinates.PointToClient(this, Presentation.Frame, _viewport, screenPoint)
                : _thumbnailPointer ?? new Point(_viewport.Viewport.Width / 2, _viewport.Viewport.Height / 2);
            if (!new Rect(_viewport.Viewport).Contains(pointer))
                pointer = new(_viewport.Viewport.Width / 2, _viewport.Viewport.Height / 2);
            var local = _viewport.TranslatePoint(pointer, _image)!.Value;
            var source =
                PinnedViewportGeometry.SourcePoint(DisplayRegion, CurrentPinZoom, RenderScaling, default, local);
            return QuickThumbnailRegionAt(source);
        }

        private PixelRect QuickThumbnailRegionAt(Point source)
        {
            var minimum = PinnedViewportGeometry.ThumbnailSourceSize(QuickThumbnailStandardDip, RenderScaling,
                PreferredQuickThumbnailZoom);
            var size = _quickThumbnailSourceSize is { } saved
                ? new(Math.Max(minimum.Width, saved.Width), Math.Max(minimum.Height, saved.Height))
                : minimum;
            return PinnedViewportGeometry.QuickThumbnail(new((int)_bitmap.Size.Width, (int)_bitmap.Size.Height),
                source, size);
        }

        private Avalonia.PixelPoint SourceScreenPoint(Point source)
        {
            var region = DisplayRegion;
            var scale = CurrentPinZoom / RenderScaling;
            return PinnedWindowCoordinates.PointToScreen(this, Presentation.Frame, _image,
                new((source.X - region.X) * scale, (source.Y - region.Y) * scale));
        }

        private void ApplyThumbnailView(PixelRect region, bool thumbnail, bool manual = false)
        {
            var previous = DisplayRegion;
            var anchor = thumbnail
                ? new Point(region.X + region.Width / 2d, region.Y + region.Height / 2d)
                : new Point(previous.X + previous.Width / 2d, previous.Y + previous.Height / 2d);
            var basis = _thumbnailLayoutBasis ??
                        (Origin: SourceScreenPoint(default), Zoom: CurrentPinZoom, Scroll: _viewport.Offset);
            var vector = _viewTransform.MatrixFor(default).Transform(anchor);
            var screenAnchor = new Avalonia.PixelPoint(
                basis.Origin.X + (int)Math.Round(vector.X * basis.Zoom),
                basis.Origin.Y + (int)Math.Round(vector.Y * basis.Zoom));
            CancelThumbnailLayout();
            var entering = !_isThumbnail && thumbnail;
            var exiting = _isThumbnail && !thumbnail;
            if (entering)
            {
                _thumbnailFullScrollPhysical = basis.Scroll * RenderScaling;
                _thumbnailNormalZoom = CurrentPinZoom;
                if (!manual) CurrentPinZoom = PreferredQuickThumbnailZoom;
            }
            else if (exiting)
            {
                _thumbnailSavedZoom = CurrentPinZoom;
                if (!_thumbnailManualRegion) _quickThumbnailSourceSize = new(previous.Width, previous.Height);
                if (_thumbnailNormalZoom > 0) CurrentPinZoom = _thumbnailNormalZoom;
            }

            var nextScroll = thumbnail ? default : _thumbnailFullScrollPhysical / RenderScaling;
            if (thumbnail)
            {
                _thumbnailRegion = region;
                _thumbnailManualRegion = manual;
            }

            _isThumbnail = thumbnail;
            RefreshThumbnail(default, false);
            var targetZoom = CurrentPinZoom;
            var requestPosition = Position;
            _thumbnailLayoutBasis = (basis.Origin, basis.Zoom, nextScroll);
            EventHandler? completion = null;
            completion = (_, _) =>
            {
                if (!ReferenceEquals(_thumbnailLayoutCompletion, completion)) return;
                if (_closed || !CurrentPinZoom.Equals(targetZoom) || DisplayRegion != region)
                {
                    CancelThumbnailLayout();
                    return;
                }

                var tolerance = 1 / RenderScaling;
                if (Math.Abs(_content.Bounds.Width + PinnedWindowPresentation.ShadeInset * 2 - Width) > tolerance ||
                    Math.Abs(_content.Bounds.Height + PinnedWindowPresentation.ShadeInset * 2 - Height) > tolerance ||
                    Math.Abs(_viewport.Extent.Width - _viewLayer.Width) > tolerance ||
                    Math.Abs(_viewport.Extent.Height - _viewLayer.Height) > tolerance) return;
                CancelThumbnailLayout();
                nextScroll = new(
                    Math.Clamp(nextScroll.X, 0, Math.Max(0, _viewport.Extent.Width - _viewport.Viewport.Width)),
                    Math.Clamp(nextScroll.Y, 0, Math.Max(0, _viewport.Extent.Height - _viewport.Viewport.Height)));
                var nextAnchor = SourceScreenPoint(anchor);
                var scrollDelta = (nextScroll - _viewport.Offset) * RenderScaling;
                nextAnchor = new(nextAnchor.X - (int)Math.Round(scrollDelta.X),
                    nextAnchor.Y - (int)Math.Round(scrollDelta.Y));
                _viewport.Offset = nextScroll;
                Position = new(Position.X + screenAnchor.X + Position.X - requestPosition.X - nextAnchor.X,
                    Position.Y + screenAnchor.Y + Position.Y - requestPosition.Y - nextAnchor.Y);
                StateChanged?.Invoke(this, EventArgs.Empty);
            };
            _thumbnailLayoutCompletion = completion;
            _content.LayoutUpdated += completion;
            PrepareThumbnailLayout();
            UpdateLayout();
            completion(this, EventArgs.Empty);
        }

        private void PrepareThumbnailLayout()
        {
            if (IsVisible) return;
            ApplyStyling();
            ApplyTemplate();
            var size = new Size(Width, Height);
            Measure(size);
            Arrange(new(default, size));
            Presentation.Frame.Measure(new(Width, Height));
            Presentation.Frame.Arrange(new(0, 0, Width, Height));
        }

        internal void RestoreHiddenState(PinnedImageState state)
        {
            RestoreState(state);
            Position = new(state.X, state.Y);
            PrepareThumbnailLayout();
            _viewport.Offset = new(state.ScrollX, state.ScrollY);
        }

        internal void PreparePendingThumbnailView()
        {
            var offset = _viewport.Offset;
            PrepareThumbnailLayout();
            _viewport.Offset = offset;
        }

        private void CancelThumbnailLayout()
        {
            if (_thumbnailLayoutCompletion is { } completion) _content.LayoutUpdated -= completion;
            _thumbnailLayoutCompletion = null;
            _thumbnailLayoutBasis = null;
        }
    }
}
