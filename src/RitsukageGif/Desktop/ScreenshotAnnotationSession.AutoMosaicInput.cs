using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private PixelPoint? _privacyDragStart;
        private PixelPoint _privacyDragEnd;
        private int[] _privacyDragTargets = [];
        private PixelPoint? _privacyHitPoint;
        private long _privacyHitVersion = -1;
        private int _privacyHitIndex = -1;
        private long _privacyHoverVersion = -1;
        private int _privacyHoverIndex = -1;
        private bool _privacyHoverSynchronous;
        private int[] _privacyHoverTargets = [];

        public bool IsPrivacyDragging => _privacyDragStart is not null;

        public bool HasPrivacyCandidate(PixelPoint point)
        {
            return FindPrivacyCandidate(point) >= 0;
        }

        public void BeginPrivacyDrag(PixelPoint point)
        {
            if (!IsPrivacyDetectionActive || PrivacyDetectionPending || IsBusy) return;
            _privacyDragStart = point;
            _privacyDragEnd = point;
            _privacyDragTargets = [];
        }

        public void MovePrivacyDrag(PixelPoint point)
        {
            if (_privacyDragStart is not { } start || IsBusy) return;
            _privacyDragEnd = point;
            var rectangle = new Rect(new(start.X, start.Y), new Point(point.X, point.Y)).Normalize()
                .Intersect(_privacyObjectBounds);
            _privacyDragTargets =
            [
                .. Enumerable.Range(0, _privacyCandidates.Length).Where(index =>
                    rectangle.Intersects(PrivacyCandidateRectangle(index))),
            ];
        }

        public void EndPrivacyDrag(PixelPoint point)
        {
            if (_privacyDragStart is null || IsBusy) return;
            MovePrivacyDrag(point);
            var targets = _privacyDragTargets;
            _privacyDragStart = null;
            _privacyDragTargets = [];
            if (targets.Length == 0) return;
            var add = targets.Any(index => !_privacySelected[index]);
            foreach (var index in targets) _privacySelected[index] = add;
            _ = SynchronizePrivacyMosaicAsync(_privacyRequest);
        }

        private int FindPrivacyCandidate(PixelPoint point)
        {
            if (_privacyHitPoint == point && _privacyHitVersion == _privacyCandidatesVersion) return _privacyHitIndex;
            _privacyHitPoint = point;
            _privacyHitVersion = _privacyCandidatesVersion;
            var selected = -1;
            var smallest = long.MaxValue;
            var bestPriority = -1;
            for (var index = 0; index < _privacyCandidates.Length; index++)
            {
                var bounds = _privacyCandidates[index].Bounds;
                var area = (long)bounds.Width * bounds.Height;
                var priority = _privacyCandidates[index].Kind switch
                {
                    PrivacyCandidateKind.Text => 0,
                    PrivacyCandidateKind.ImageStructure or PrivacyCandidateKind.ImageBlock
                        or PrivacyCandidateKind.RelatedStructure => 3,
                    _ => 4,
                };
                if (!bounds.Contains(point) || area > smallest ||
                    (area == smallest && priority <= bestPriority)) continue;
                selected = index;
                smallest = area;
                bestPriority = priority;
            }

            _privacyHitIndex = selected;
            return selected;
        }

        private int[] PrivacyHoverTargets(int selectedIndex)
        {
            var synchronous = Properties.AutoMosaicSynchronous;
            if (_privacyHoverIndex == selectedIndex && _privacyHoverSynchronous == synchronous &&
                _privacyHoverVersion == _privacyCandidatesVersion) return _privacyHoverTargets;
            _privacyHoverIndex = selectedIndex;
            _privacyHoverSynchronous = synchronous;
            _privacyHoverVersion = _privacyCandidatesVersion;
            var candidate = _privacyCandidates[selectedIndex];
            _privacyHoverTargets =
            [
                .. Enumerable.Range(0, _privacyCandidates.Length).Where(index =>
                    _privacyCandidates[index].Bounds == candidate.Bounds ||
                    (synchronous && PrivacyCandidatesMatch(index, selectedIndex))),
            ];
            return _privacyHoverTargets;
        }

        private Rect PrivacyCandidateRectangle(int index)
        {
            var bounds = _privacyCandidates[index].Bounds;
            return new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        private void DrawPrivacyTargets(DrawingContext context, IReadOnlyList<int> targets, bool add, double scale)
        {
            var color = add ? Color.FromRgb(0x67, 0xc2, 0x3a) : Color.FromRgb(0xe5, 0x3e, 0x30);
            var fill = new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B));
            var pen = new Pen(new SolidColorBrush(color), scale);
            foreach (var index in targets) context.DrawRectangle(fill, pen, PrivacyCandidateRectangle(index));
        }
    }
}
