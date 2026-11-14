namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Func<PinnedImageWindow, PinnedImageArrangement, Task>? ArrangeSelectionRequested { get; set; }
        public Func<PinnedImageWindow, Task>? SaveSelectionRequested { get; set; }
        private bool CanArrangeSelection => !_closed;

        private PixelRect SelectionLayoutBounds
        {
            get
            {
                var width = (int)Math.Clamp(Math.Ceiling(Math.Max(ClientSize.Width, Width) * RenderScaling), 1,
                    int.MaxValue);
                var height = (int)Math.Clamp(Math.Ceiling(Math.Max(ClientSize.Height, Height) * RenderScaling), 1,
                    int.MaxValue);
                return new(Position.X, Position.Y, width, height);
            }
        }

        private bool ExecuteMultiSelectionShortcut(string action)
        {
            if (action == "selection-save")
            {
                _ = RunAsync(() => SaveSelectionRequested?.Invoke(this) ?? Task.CompletedTask);
                return true;
            }

            if (PinnedImageArrangementCommands.TryResolve(action, out var arrangement))
            {
                _ = RunAsync(() => ArrangeSelectionRequested?.Invoke(this, arrangement) ?? Task.CompletedTask);
                return true;
            }

            return false;
        }
    }
}
