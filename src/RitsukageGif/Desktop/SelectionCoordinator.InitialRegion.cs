namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal PixelRect? InitialRegion { get; init; }
        internal WindowTarget? InitialTarget { get; init; }
        internal bool WholeInitialTarget { get; init; }

        private void ApplyInitialRegion()
        {
            if (InitialRegion is not { IsEmpty: false } requested) return;
            if (InitialTarget is { } initialTarget)
                _selectedTarget = _targets.FirstOrDefault(candidate => candidate.Handle == initialTarget.Handle)
                                  ?? throw new CaptureSourceLostException();
            if (WholeInitialTarget)
            {
                if (_selectedTarget is not { } wholeTarget) throw new CaptureSourceLostException();
                requested = WindowSource ? wholeTarget.Client : wholeTarget.Bounds;
            }

            var region = requested.Intersect(SelectionBounds());
            if (WindowSource && !UsesLocalSource)
            {
                var center = new PixelPoint(requested.X + requested.Width / 2, requested.Y + requested.Height / 2);
                _selectedTarget ??= _targets.FirstOrDefault(candidate => candidate.Bounds.Contains(center));
                region = _selectedTarget is { } target ? region.Intersect(target.Client) : default;
            }

            if (region.IsEmpty)
            {
                if (AutoConfirm) throw new InvalidOperationException(UiText.Get("InvalidRecording"));
                return;
            }

            _selectionIsWholeTarget = false;
            _state.Set(region);
        }
    }
}
