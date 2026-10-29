using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private PinnedImageWindow[] SelectionSnapshot(PinnedImageWindow source)
        {
            return _selectedImages.Contains(source) && _selectedImages.Count > 1
                ? [.. _windows.Where(window => _selectedImages.Contains(window))]
                : [];
        }

        private bool CanArrangeSelection(PinnedImageWindow source, PinnedImageArrangement arrangement)
        {
            var images = SelectionSnapshot(source);
            var minimum = arrangement is PinnedImageArrangement.DistributeHorizontal
                or PinnedImageArrangement.DistributeVertical
                ? 3
                : 2;
            return images.Length >= minimum && images.All(window => window.CanArrangeSelection);
        }

        private void ArrangeSelection(PinnedImageWindow source, PinnedImageArrangement arrangement)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanArrangeSelection(source, arrangement)) return;
            var images = SelectionSnapshot(source);
            var original = images.Select(window => window.Position).ToArray();
            PixelRect[] bounds = [.. images.Select(window => window.SelectionLayoutBounds)];
            var positions = PinnedImageLayoutEngine.Arrange(bounds,
                arrangement, source.Screens.ScreenFromWindow(source)?.WorkingArea.Width ?? 1920);
            try
            {
                for (var index = 0; index < images.Length; index++)
                    images[index].Position = new(positions[index].X, positions[index].Y);
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                for (var index = 0; index < images.Length; index++)
                    try
                    {
                        images[index].Position = original[index];
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
