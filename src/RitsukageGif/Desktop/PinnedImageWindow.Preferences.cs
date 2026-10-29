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

        private bool IsOperationLocked(PinnedLockedOperations operation)
        {
            return IsLocked && (CurrentPreferences.LockedOperations & operation) != 0;
        }

        public void RefreshPreferencePresentation()
        {
            RefreshWindowShadow();
            EnsureTransformCommands();
            if (!CanSelectImageText) ResetTextRecognition();
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
                            Data = Geometry.Parse("M5,10 H19 V22 H5 Z M8,10 V6 A4,4 0 0 1 16,6 V10"),
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
        }

        private Point ZoomAnchor(Point point, PinnedZoomAnchor anchor)
        {
            return anchor switch
            {
                PinnedZoomAnchor.TopLeft => new(0, 0),
                PinnedZoomAnchor.Pointer => point,
                PinnedZoomAnchor.Center => _viewport.Bounds.Center,
                _ => throw new ArgumentOutOfRangeException(nameof(anchor)),
            };
        }
    }
}
