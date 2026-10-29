namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private IReadOnlyList<WindowControlTarget> _controlHierarchy = [];
        private int _controlRevision;

        private WindowTarget? CurrentControlTarget => WindowSource
            ? _selectedTarget ?? _hoveredTarget
            : _targets.FirstOrDefault(candidate => candidate.Bounds.Contains(_pointer));

        private async Task<IReadOnlyList<WindowControlTarget>> FindControlHierarchyAsync(WindowTarget target,
            PixelPoint point, CancellationToken token)
        {
            var nativeTask = Task.Run(() => WindowControlLocator.FindHierarchy(target, point, token), token);
            var accessibilityTask = WindowsAccessibilityLocator.FindHierarchyAsync(target, point, token);
            await Task.WhenAll(nativeTask, accessibilityTask);
            token.ThrowIfCancellationRequested();
            var limit = WindowSource ? target.Client : target.Bounds;
            var hierarchy = new List<WindowControlTarget>();
            foreach (var candidate in nativeTask.Result.Concat(accessibilityTask.Result)
                         .OrderByDescending(candidate => (long)candidate.Bounds.Width * candidate.Bounds.Height))
            {
                var bounds = candidate.Bounds.Intersect(limit);
                if (bounds.IsEmpty || !bounds.Contains(point)) continue;
                if (hierarchy.Count > 0 && hierarchy[^1].Bounds == bounds) continue;
                hierarchy.Add(candidate with { Bounds = bounds });
                limit = bounds;
            }

            return hierarchy;
        }

        private void InvalidateControlDetection()
        {
            _controlRevision++;
            _controlBounds = default;
            _controlHierarchy = [];
            _candidateHierarchy = null;
        }
    }
}
