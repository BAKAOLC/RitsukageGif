using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private bool _isThumbnail;
        private bool _thumbnailManualRegion;
        private Rect _thumbnailRegion;
        private Size _thumbnailNormalSize;
        private Size _thumbnailSurfaceSize;
        private Vector _thumbnailNormalScroll;
        private double _thumbnailNormalZoom = 1;
        private double _thumbnailSavedZoom = 1;
        private bool _thumbnailHasSavedZoom;
        private bool _applyingThumbnail;
        private PinnedDocumentThumbnailRequest? _thumbnailRequest;
        private (Point Source, Avalonia.PixelPoint Screen)? _thumbnailPendingAnchor;
        private Vector? _thumbnailPendingScroll;

        public void SetThumbnailDisplay(bool value, Avalonia.PixelPoint? screenAnchor = null)
        {
            if (_closed || value == (_thumbnailRequest?.Enabled ?? _isThumbnail)) return;
            if (Document is PinnedFormulaDocument && _documentBitmap is null)
            {
                _thumbnailRequest = value == _isThumbnail
                    ? null
                    : new(value, screenAnchor?.X, screenAnchor?.Y);
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _thumbnailRequest = null;
            EnsureDocumentSceneLayout();
            EndDocumentMark();
            if (value == _isThumbnail) return;
            EnsureDocumentSceneLayout();
            if (value)
            {
                var anchor = DocumentScenePoint(screenAnchor is { } point
                    ? PinnedWindowCoordinates.PointToClient(this, Presentation.Frame, _content, point)
                    : new(_content.Bounds.Width / 2, _content.Bounds.Height / 2));
                BeginDocumentThumbnail(anchor);
            }
            else
            {
                EndDocumentThumbnail();
            }
        }

        private void ToggleDocumentThumbnail()
        {
            if (IsInteractionLocked || IsMarkEditing) return;
            SetThumbnailDisplay(!(_thumbnailRequest?.Enabled ?? _isThumbnail));
        }

        private void BeginDocumentThumbnail(Point sourceAnchor, Rect? selectedRegion = null)
        {
            var sourceScreenOrigin = PinnedWindowCoordinates.PointToScreen(this, Presentation.Frame, _content,
                DocumentViewportPoint(default));
            var sourceViewScale = DocumentSceneViewScale;
            if (!_isThumbnail)
            {
                _thumbnailNormalSize = new(Width, Height);
                _thumbnailSurfaceSize = _surface.Bounds.Size;
                _thumbnailNormalScroll = ContentScrollOffset();
                _thumbnailNormalZoom = _zoom;
                _surface.Width = _thumbnailSurfaceSize.Width;
                _surface.Height = _thumbnailSurfaceSize.Height;
                if (_thumbnailHasSavedZoom && selectedRegion is null) ApplyDocumentZoom(_thumbnailSavedZoom);
            }

            var scene = MeasureDocumentScene();
            var scaling = RenderScaling;
            var preferences = PreferencesProvider();
            var standard = new PixelSize(
                preferences.QuickThumbnailWidth ?? PinnedViewportGeometry.DefaultThumbnailSourceSize,
                preferences.QuickThumbnailHeight ?? PinnedViewportGeometry.DefaultThumbnailSourceSize);
            var sourceSize = new PixelSize(Math.Max(1, (int)Math.Ceiling(scene.Width * scaling)),
                Math.Max(1, (int)Math.Ceiling(scene.Height * scaling)));
            if (selectedRegion is null)
            {
                var minimumScale = PinnedViewportGeometry.MinimumThumbnailScale(sourceSize, standard, scaling);
                if (DocumentSceneViewScale < minimumScale)
                    ApplyDocumentZoom(_zoom * minimumScale / DocumentSceneViewScale);
            }

            var previous = _thumbnailRegion;
            var size = !_thumbnailManualRegion && previous is { Width: > 0, Height: > 0 }
                ? new(Math.Max(1, (int)Math.Round(previous.Width * scaling)),
                    Math.Max(1, (int)Math.Round(previous.Height * scaling)))
                : PinnedViewportGeometry.ThumbnailSourceSize(standard, scaling, DocumentSceneViewScale);
            var quick = PinnedViewportGeometry.QuickThumbnail(sourceSize, sourceAnchor * scaling, size);
            var region = selectedRegion ?? new Rect(quick.X / scaling, quick.Y / scaling,
                quick.Width / scaling, quick.Height / scaling);
            region = region.Intersect(new(scene));
            if (region.Width <= 0 || region.Height <= 0) return;
            var screenCenter = new Avalonia.PixelPoint(
                sourceScreenOrigin.X + (int)Math.Round(region.Center.X * sourceViewScale * scaling),
                sourceScreenOrigin.Y + (int)Math.Round(region.Center.Y * sourceViewScale * scaling));
            _thumbnailPendingAnchor = (region.Center, screenCenter);
            _thumbnailRegion = region;
            _thumbnailManualRegion = selectedRegion is not null;
            _isThumbnail = true;
            ApplyDocumentThumbnailGeometry();
            AlignDocumentThumbnailLayout();
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private Point DocumentViewportPoint(Point source)
        {
            var origin = DocumentSceneViewportOrigin;
            return new((source.X - origin.X) * DocumentSceneViewScale,
                (source.Y - origin.Y) * DocumentSceneViewScale);
        }

        private void ApplyDocumentThumbnailGeometry()
        {
            if (!_isThumbnail || _applyingThumbnail || !DocumentSceneCoordinatesAvailable ||
                (Document is PinnedFormulaDocument && _documentBitmap is null)) return;
            _applyingThumbnail = true;
            try
            {
                _layout.RenderTransform = null;
                _content.ClipToBounds = true;
                MinWidth = MinHeight = PinnedWindowPresentation.ShadeInset * 2 + 1;
                Width = _thumbnailRegion.Width * DocumentSceneViewScale + PinnedWindowPresentation.ShadeInset * 2;
                Height = _thumbnailRegion.Height * DocumentSceneViewScale + PinnedWindowPresentation.ShadeInset * 2;
                _viewport.Offset = default;
                if (_textView is { } text)
                    text.ScrollOffset = new(_thumbnailRegion.X, _thumbnailRegion.Y);
                EnsureDocumentSceneLayout();
                var origin = DocumentSceneViewportOrigin;
                var scale = DocumentSceneViewScale;
                _layout.RenderTransform = new TranslateTransform(
                    (origin.X - _thumbnailRegion.X) * scale, (origin.Y - _thumbnailRegion.Y) * scale);
                _textView?.SetSelectable(Selectable && PreferencesProvider().ThumbnailSelectableText);
                DocumentAnnotationLayoutChanged(false);
                EnsureDocumentSceneLayout();
            }
            finally
            {
                _applyingThumbnail = false;
            }
        }

        private void EndDocumentThumbnail()
        {
            var sourceCenter = _thumbnailRegion.Center;
            var center = PinnedWindowCoordinates.PointToScreen(this, Presentation.Frame, _content,
                DocumentViewportPoint(sourceCenter));
            _thumbnailPendingAnchor = (sourceCenter, center);
            _thumbnailPendingScroll = _thumbnailNormalScroll;
            _thumbnailSavedZoom = _zoom;
            _thumbnailHasSavedZoom = true;
            _isThumbnail = false;
            _layout.RenderTransform = null;
            _content.ClipToBounds = false;
            _surface.Width = _surface.Height = double.NaN;
            MinWidth = 120 + PinnedWindowPresentation.ShadeInset * 2;
            MinHeight = 72 + PinnedWindowPresentation.ShadeInset * 2;
            Width = _thumbnailNormalSize.Width;
            Height = _thumbnailNormalSize.Height;
            SetZoom(_thumbnailNormalZoom);
            EnsureDocumentSceneLayout();
            SetContentScrollOffset(_thumbnailNormalScroll);
            EnsureDocumentSceneLayout();
            AlignDocumentThumbnailLayout();
            _textView?.SetSelectable(Selectable);
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AlignDocumentThumbnailLayout()
        {
            if (!_isThumbnail && _thumbnailPendingAnchor is null && _thumbnailPendingScroll is null) return;
            if (_closed || _applyingThumbnail || !DocumentSceneCoordinatesAvailable) return;
            if (_isThumbnail)
            {
                const double inset = PinnedWindowPresentation.ShadeInset * 2;
                if (Math.Abs(Width - (_thumbnailRegion.Width * DocumentSceneViewScale + inset)) > 1 / RenderScaling ||
                    Math.Abs(Height - (_thumbnailRegion.Height * DocumentSceneViewScale + inset)) > 1 / RenderScaling)
                {
                    ApplyDocumentThumbnailGeometry();
                    return;
                }

                var origin = DocumentSceneViewportOrigin;
                var difference = origin - _thumbnailRegion.TopLeft;
                if (Math.Abs(difference.X) > .001 || Math.Abs(difference.Y) > .001)
                {
                    var previous = _layout.RenderTransform as TranslateTransform;
                    _layout.RenderTransform = new TranslateTransform(
                        (previous?.X ?? 0) + difference.X * DocumentSceneViewScale,
                        (previous?.Y ?? 0) + difference.Y * DocumentSceneViewScale);
                    DocumentAnnotationLayoutChanged(false);
                }
            }

            if (Math.Abs(_content.Bounds.Width - (Width - PinnedWindowPresentation.ShadeInset * 2)) >
                1 / RenderScaling ||
                Math.Abs(_content.Bounds.Height - (Height - PinnedWindowPresentation.ShadeInset * 2)) >
                1 / RenderScaling)
                return;
            if (_thumbnailPendingScroll is { } scroll)
            {
                _thumbnailPendingScroll = null;
                SetContentScrollOffset(scroll);
            }

            if (_thumbnailPendingAnchor is { } anchor)
            {
                _thumbnailPendingAnchor = null;
                var next = PinnedWindowCoordinates.PointToScreen(this, Presentation.Frame, _content,
                    DocumentViewportPoint(anchor.Source));
                Position += anchor.Screen - next;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void DocumentThumbnailSizeChanged()
        {
            if (!_isThumbnail || _applyingThumbnail || !DocumentSceneCoordinatesAvailable) return;
            var scale = DocumentSceneViewScale;
            const double inset = PinnedWindowPresentation.ShadeInset * 2;
            if (Math.Abs(Width - (_thumbnailRegion.Width * scale + inset)) <= 1 / RenderScaling &&
                Math.Abs(Height - (_thumbnailRegion.Height * scale + inset)) <= 1 / RenderScaling) return;
            var scene = MeasureDocumentScene();
            _thumbnailRegion = new Rect(_thumbnailRegion.Position,
                    new Size(Math.Max(1, Width - inset) / scale, Math.Max(1, Height - inset) / scale))
                .Intersect(new(scene));
            ApplyDocumentThumbnailGeometry();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private PinnedDocumentThumbnailState? CaptureDocumentThumbnail()
        {
            if (_thumbnailRegion.Width <= 0 || _thumbnailRegion.Height <= 0) return null;
            return new(_isThumbnail, _thumbnailNormalSize.Width, _thumbnailNormalSize.Height,
                _thumbnailSurfaceSize.Width, _thumbnailSurfaceSize.Height,
                _thumbnailNormalScroll.X, _thumbnailNormalScroll.Y,
                _isThumbnail ? _thumbnailNormalZoom : _zoom,
                _isThumbnail ? _zoom : _thumbnailSavedZoom,
                _thumbnailRegion.X, _thumbnailRegion.Y, _thumbnailRegion.Width, _thumbnailRegion.Height,
                _thumbnailManualRegion);
        }

        private void RestoreDocumentThumbnail(PinnedDocumentThumbnailState? state)
        {
            if (state is null) return;
            _thumbnailNormalSize = new(state.NormalWidth, state.NormalHeight);
            _thumbnailSurfaceSize = new(state.SurfaceWidth, state.SurfaceHeight);
            _thumbnailNormalScroll = new(state.ScrollX, state.ScrollY);
            _thumbnailNormalZoom = state.NormalZoom;
            _thumbnailSavedZoom = state.ThumbnailZoom;
            _thumbnailHasSavedZoom = true;
            _thumbnailRegion = new(state.RegionX, state.RegionY, state.RegionWidth, state.RegionHeight);
            _thumbnailManualRegion = state.ManualRegion;
            if (!state.Enabled) return;
            _surface.Width = state.SurfaceWidth;
            _surface.Height = state.SurfaceHeight;
            _isThumbnail = true;
            ApplyDocumentZoom(state.ThumbnailZoom);
            ApplyDocumentThumbnailGeometry();
        }

        private void ApplyDocumentThumbnailRequest()
        {
            if (_thumbnailRequest is not { } request ||
                this is { Document: PinnedFormulaDocument, _documentBitmap: null }) return;
            _thumbnailRequest = null;
            SetThumbnailDisplay(request.Enabled, request is { ScreenX: { } x, ScreenY: { } y }
                ? new Avalonia.PixelPoint(x, y)
                : null);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
