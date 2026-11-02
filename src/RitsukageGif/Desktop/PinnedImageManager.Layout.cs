using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private IPinnedWindow[] SelectionSnapshot(IPinnedWindow source)
        {
            return _selectedImages.Contains(source) && _selectedImages.Count > 1
                ? [.. AllWindows.Where(window => _selectedImages.Contains(window))]
                : [];
        }

        private bool CanArrangeSelection(IPinnedWindow source, PinnedImageArrangement arrangement)
        {
            var images = SelectionSnapshot(source);
            var minimum = arrangement is PinnedImageArrangement.DistributeHorizontal
                or PinnedImageArrangement.DistributeVertical
                ? 3
                : 2;
            return images.Length >= minimum && images.All(window => window.CanArrangeSelection);
        }

        private void ArrangeSelection(IPinnedWindow source, PinnedImageArrangement arrangement)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanArrangeSelection(source, arrangement)) return;
            var images = SelectionSnapshot(source);
            var original = images.Select(window => window.Window.Position).ToArray();
            PixelRect[] bounds = [.. images.Select(window => window.SelectionLayoutBounds)];
            var positions = PinnedImageLayoutEngine.Arrange(bounds,
                arrangement, source.Window.Screens.ScreenFromWindow(source.Window)?.WorkingArea.Width ?? 1920);
            try
            {
                for (var index = 0; index < images.Length; index++)
                    images[index].Window.Position = new(positions[index].X, positions[index].Y);
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                for (var index = 0; index < images.Length; index++)
                    try
                    {
                        images[index].Window.Position = original[index];
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }

                if (errors.Count > 1) throw new AggregateException(errors);
                throw;
            }

            ScheduleSave();
        }
    }
}
