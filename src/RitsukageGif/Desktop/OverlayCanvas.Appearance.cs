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
            _candidatePen = new Pen(new SolidColorBrush(Color.FromUInt32(preferences.LockedWindowColor)),
                preferences.SelectionBorderWidth, DashStyle.Dash);
            _selectionBrush =
                new SolidColorBrush(Color.FromUInt32(preferences.SelectionBorderColor ?? preferences.AccentColor));
            _selectedPen = new Pen(_selectionBrush, preferences.SelectionBorderWidth);
            if (!preferences.AnimateSelection)
            {
                _selectionAnimationStart = 0;
                _selectionAnimationTick?.Dispose();
                _selectionAnimationTick = null;
            }

            InvalidateVisual();
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
