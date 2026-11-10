using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private Border? _lockIndicator;

        private bool IsOperationLocked(PinnedLockRestrictions operation)
        {
            return IsLocked && (CurrentPreferences.LockRestrictions & operation) != 0;
        }

        public void RefreshPreferencePresentation()
        {
            Presentation.Title.Refresh();
            Presentation.Attention.Refresh();
            RefreshWindowShadow();
            PinnedBitmapSampling.Apply(_image, CurrentPreferences.SmoothScaling);
            if (_hdrPreview is { } hdrPreview) hdrPreview.SmoothScaling = CurrentPreferences.SmoothScaling;
            EnsureTransformCommands();
            if (!CanSelectImageText) ClearTextSelection();
            if (!CurrentPreferences.ShowTextPopupMenu) _textSelectionMenu?.IsOpen = false;
            ShowInTaskbar = !_canRestoreHidden() || (!Topmost && CurrentPreferences.ShowTaskbarWhenNotTopmost);
            if (IsLocked && CurrentPreferences.ShowLockIndicator)
            {
                if (_lockIndicator is null)
                {
                    _lockIndicator = new()
                    {
                        IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top, Margin = new(4), Padding = new(4),
                        Background = AppTheme.SurfaceTint, CornerRadius = new(4),
                        Child = new Path
                        {
                            Width = 14, Height = 14, Stroke = AppTheme.Ink, StrokeThickness = 1.6,
                            Stretch = Stretch.Uniform,
                            Data = IconCatalog.GetGeometry("PinnedImageLockIndicator"),
                        },
                    };
                    _content.Children.Add(_lockIndicator);
                }

                _lockIndicator.IsVisible = true;
            }
            else
            {
                _lockIndicator?.IsVisible = false;
            }

            UpdateSize();
        }

        private Point ZoomAnchor(Point point, PinnedZoomAnchor anchor)
        {
            return anchor switch
            {
                PinnedZoomAnchor.TopLeft => new(PinnedWindowPresentation.ShadeInset,
                    PinnedWindowPresentation.ShadeInset),
                PinnedZoomAnchor.Pointer => point,
                PinnedZoomAnchor.Center => new(_viewport.Bounds.Center.X + PinnedWindowPresentation.ShadeInset,
                    _viewport.Bounds.Center.Y + PinnedWindowPresentation.ShadeInset),
                _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
            };
        }
    }
}
