using Avalonia;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportCoverage
    {
        private readonly Rect _display;
        private readonly Rect? _targetClip;

        internal PinnedViewportCoverage(Rect sourceDisplayRegion, Rect? targetClip, Matrix sourceToTarget)
        {
            _display = sourceDisplayRegion.TransformToAABB(sourceToTarget);
            if (targetClip is { } clip)
            {
                var left = Math.Floor(clip.Left + .5);
                var top = Math.Floor(clip.Top + .5);
                _targetClip = new(left, top, Math.Floor(clip.Right + .5) - left,
                    Math.Floor(clip.Bottom + .5) - top);
            }
            else
            {
                _targetClip = null;
            }
        }

        internal double At(int column, int row)
        {
            if (_targetClip is { } clip &&
                (column < clip.Left || column >= clip.Right || row < clip.Top || row >= clip.Bottom)) return 0;
            return Math.Max(0, Math.Min(column + 1, _display.Right) - Math.Max(column, _display.Left)) *
                   Math.Max(0, Math.Min(row + 1, _display.Bottom) - Math.Max(row, _display.Top));
        }
    }
}
