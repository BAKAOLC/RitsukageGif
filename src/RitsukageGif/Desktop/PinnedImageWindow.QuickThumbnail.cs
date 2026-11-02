using Avalonia;
using Avalonia.Controls;
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

        private void ConfigureQuickThumbnailPointer()
        {
            AddHandler(PointerMovedEvent, (_, args) => _thumbnailPointer = args.GetPosition(_viewport),
                RoutingStrategies.Tunnel);
        }

        private PixelRect InitialQuickThumbnailRegion()
        {
            var pointer = PinnedGroupPointer.Read() is { } screenPoint
                ? _viewport.PointToClient(screenPoint)
                : _thumbnailPointer ?? new Point(_viewport.Viewport.Width / 2, _viewport.Viewport.Height / 2);
            if (!new Rect(_viewport.Viewport).Contains(pointer))
                pointer = new(_viewport.Viewport.Width / 2, _viewport.Viewport.Height / 2);
            var visual = (Control)_viewport.Content!;
            var local = _viewport.TranslatePoint(pointer, visual)!.Value;
            var source = PinnedViewportGeometry.SourcePoint(DisplayRegion, _zoom, RenderScaling, default, local);
            var size = _quickThumbnailSourceSize ?? new PixelSize(
                CurrentPreferences.QuickThumbnailWidth ?? PinnedViewportGeometry.DefaultThumbnailSourceSize,
                CurrentPreferences.QuickThumbnailHeight ?? PinnedViewportGeometry.DefaultThumbnailSourceSize);
            return PinnedViewportGeometry.QuickThumbnail(new((int)_bitmap.Size.Width, (int)_bitmap.Size.Height), source,
                size);
        }

        private Avalonia.PixelPoint SourceScreenPoint(Point source)
        {
            var visual = (Control)_viewport.Content!;
            var region = DisplayRegion;
            var scale = _zoom / RenderScaling;
            return visual.PointToScreen(new((source.X - region.X) * scale, (source.Y - region.Y) * scale));
        }

        private void ApplyThumbnailView(PixelRect region, bool thumbnail, bool manual = false)
        {
            var previous = DisplayRegion;
            var anchor = thumbnail
                ? new Point(region.X + region.Width / 2d, region.Y + region.Height / 2d)
                : new Point(previous.X + previous.Width / 2d, previous.Y + previous.Height / 2d);
            var basis = _thumbnailLayoutBasis ??
                        (Origin: SourceScreenPoint(default), Zoom: _zoom, Scroll: _viewport.Offset);
            var screenAnchor = new Avalonia.PixelPoint(
                basis.Origin.X + (int)Math.Round(anchor.X * basis.Zoom),
                basis.Origin.Y + (int)Math.Round(anchor.Y * basis.Zoom));
            CancelThumbnailLayout();
            var entering = !_isThumbnail && thumbnail;
            var exiting = _isThumbnail && !thumbnail;
            if (entering)
            {
                _thumbnailFullScrollPhysical = basis.Scroll * RenderScaling;
                _thumbnailNormalZoom = _zoom;
                if (!manual && _thumbnailSavedZoom > 0) _zoom = _thumbnailSavedZoom;
            }
            else if (exiting)
            {
                _thumbnailSavedZoom = _zoom;
                if (!_thumbnailManualRegion) _quickThumbnailSourceSize = new(previous.Width, previous.Height);
                if (_thumbnailNormalZoom > 0) _zoom = _thumbnailNormalZoom;
            }

            var nextScroll = thumbnail ? default : _thumbnailFullScrollPhysical / RenderScaling;
            if (thumbnail)
            {
                _thumbnailRegion = region;
                _thumbnailManualRegion = manual;
            }

            _isThumbnail = thumbnail;
            RefreshThumbnail(default, false);
            var targetZoom = _zoom;
            var requestPosition = Position;
            _thumbnailLayoutBasis = (basis.Origin, basis.Zoom, nextScroll);
            EventHandler? completion = null;
            completion = (_, _) =>
            {
                if (!ReferenceEquals(_thumbnailLayoutCompletion, completion)) return;
                if (_closed || !_zoom.Equals(targetZoom) || DisplayRegion != region)
                {
                    CancelThumbnailLayout();
                    return;
                }

                var tolerance = 1 / RenderScaling;
                if (Math.Abs(_content.Bounds.Width - Width) > tolerance ||
                    Math.Abs(_content.Bounds.Height - Height) > tolerance ||
                    Math.Abs(_viewport.Extent.Width - _image.Width) > tolerance ||
                    Math.Abs(_viewport.Extent.Height - _image.Height) > tolerance) return;
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
            UpdateLayout();
            completion(this, EventArgs.Empty);
        }

        private void CancelThumbnailLayout()
        {
            if (_thumbnailLayoutCompletion is { } completion) _content.LayoutUpdated -= completion;
            _thumbnailLayoutCompletion = null;
            _thumbnailLayoutBasis = null;
        }
    }
}
