using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private bool _thumbnailRegionSelection;
        private MouseAction? _thumbnailDragAction;
        private Point _thumbnailDragStart;
        private Point _thumbnailDragSource;
        private Rect _thumbnailDragRegion;
        private Size _thumbnailDragSceneSize;
        private Border? _thumbnailRegionOutline;

        private void SelectDocumentThumbnailRegion()
        {
            if (IsMarkEditing || IsInteractionLocked) return;
            _thumbnailRegionSelection = true;
        }

        private bool BeginDocumentThumbnailPointer(PointerPressedEventArgs args)
        {
            if (!_thumbnailRegionSelection || !args.GetCurrentPoint(_content).Properties.IsLeftButtonPressed)
                return false;
            if (!StartDocumentThumbnailDrag(MouseAction.SelectThumbnailRegion, args.GetPosition(_content)))
                return false;
            args.Pointer.Capture(_content);
            args.Handled = true;
            return true;
        }

        private bool StartDocumentThumbnailDrag(MouseAction action, Point point)
        {
            if (action is not (MouseAction.SelectThumbnailRegion or MouseAction.MoveThumbnailRegion) ||
                IsInteractionLocked || IsMarkEditing || (action == MouseAction.MoveThumbnailRegion && !_isThumbnail))
                return false;
            _thumbnailDragAction = action;
            _thumbnailDragStart = point;
            _thumbnailDragSource = DocumentScenePoint(point);
            _thumbnailDragRegion = _thumbnailRegion;
            _thumbnailDragSceneSize = MeasureDocumentScene();
            return true;
        }

        private Rect DocumentThumbnailDragRegion(Point point)
        {
            var source = DocumentScenePoint(point);
            return new Rect(_thumbnailDragSource, source).Normalize().Intersect(new(_thumbnailDragSceneSize));
        }

        private bool MoveDocumentThumbnailPointer(PointerEventArgs args)
        {
            if (_thumbnailDragAction is not { } action) return false;
            var point = args.GetPosition(_content);
            if (action == MouseAction.MoveThumbnailRegion)
            {
                var delta = (point - _thumbnailDragStart) / DocumentSceneViewScale;
                var scene = _thumbnailDragSceneSize;
                _thumbnailRegion = new(Math.Clamp(_thumbnailDragRegion.X - delta.X, 0,
                        Math.Max(0, scene.Width - _thumbnailDragRegion.Width)),
                    Math.Clamp(_thumbnailDragRegion.Y - delta.Y, 0,
                        Math.Max(0, scene.Height - _thumbnailDragRegion.Height)),
                    _thumbnailDragRegion.Width, _thumbnailDragRegion.Height);
                ApplyDocumentThumbnailGeometry();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                var region = DocumentThumbnailDragRegion(point);
                _thumbnailRegionOutline ??= new()
                {
                    BorderBrush = AppTheme.Accent, BorderThickness = new(1), IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                };
                if (!_content.Children.Contains(_thumbnailRegionOutline))
                    _content.Children.Add(_thumbnailRegionOutline);
                var origin = DocumentViewportPoint(region.TopLeft);
                _thumbnailRegionOutline.Margin = new(origin.X, origin.Y, 0, 0);
                _thumbnailRegionOutline.Width = region.Width * DocumentSceneViewScale;
                _thumbnailRegionOutline.Height = region.Height * DocumentSceneViewScale;
            }

            args.Handled = true;
            return true;
        }

        private bool FinishDocumentThumbnailPointer(PointerReleasedEventArgs args)
        {
            if (_thumbnailDragAction is not { } action) return false;
            var region = action == MouseAction.SelectThumbnailRegion
                ? DocumentThumbnailDragRegion(args.GetPosition(_content))
                : default;
            CancelDocumentThumbnailPointer();
            args.Pointer.Capture(null);
            if (region is { Width: > 0, Height: > 0 }) BeginDocumentThumbnail(region.Center, region);
            args.Handled = true;
            return true;
        }

        private void CancelDocumentThumbnailPointer()
        {
            _thumbnailDragAction = null;
            _thumbnailRegionSelection = false;
            if (_thumbnailRegionOutline is not null) _content.Children.Remove(_thumbnailRegionOutline);
        }
    }
}
