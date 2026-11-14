using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class OverlayCanvas
    {
        private PixelRect _animatedRegion;
        private AppearancePreferences? _appearance;
        private long _selectionAnimationStart;
        private IDisposable? _selectionAnimationTick;

        internal void ApplyAppearance(AppearancePreferences preferences)
        {
            if (_appearance == preferences) return;
            _appearance = preferences;
            _dimBrush = new SolidColorBrush(Color.FromUInt32(preferences.SelectionMaskColor));
            _selectionBrush =
                new SolidColorBrush(Color.FromUInt32(preferences.EffectiveSelectionBorderColor));
            UpdateSelectionPens(new SolidColorBrush(Color.FromUInt32(preferences.LockedWindowColor)));
            if (!preferences.AnimateSelection)
            {
                _selectionAnimationStart = 0;
                _selectionAnimationTick?.Dispose();
                _selectionAnimationTick = null;
            }

            InvalidateVisual();
        }

        private void UpdateSelectionPens(IBrush? candidateBrush = null)
        {
            var width = _appearance?.SelectionBorderWidth ?? AppearancePreferences.DefaultSelectionBorderWidth;
            var thickness = Math.Max(1, width) / Scale;
            _candidatePen = new(candidateBrush ?? _candidatePen.Brush, thickness, DashStyle.Dash);
            _selectedPen = new Pen(_selectionBrush, thickness);
        }

        private double SelectionBorderOpacity()
        {
            var animate = _appearance?.AnimateSelection == true &&
                          Snapshot?.Phase is not (SelectionPhase.Pressed or SelectionPhase.Dragging) && !Manipulating;
            if (_animatedRegion != Selected)
            {
                _animatedRegion = Selected;
                _selectionAnimationStart = animate && !Selected.IsEmpty ? Stopwatch.GetTimestamp() : 0;
            }

            if (!animate || _selectionAnimationStart == 0) return 1;
            var progress = Stopwatch.GetElapsedTime(_selectionAnimationStart).TotalMilliseconds / 120;
            if (progress >= 1) return 1;
            _selectionAnimationTick ??= DispatcherTimer.RunOnce(() =>
            {
                _selectionAnimationTick = null;
                InvalidateVisual();
            }, TimeSpan.FromMilliseconds(16));
            return .65 + .35 * progress;
        }
    }
}
