using Avalonia.Controls;
using Avalonia.Platform;
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

        private async Task ArrangeSelection(IPinnedWindow source, PinnedImageArrangement arrangement)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!source.CanArrangeSelection) return;
            var screen = ArrangementScreen(source, source.Window.Screens);
            if (screen is null) return;
            var images = SelectionSnapshot(source)
                .Where(window => window.CanArrangeSelection &&
                                 ArrangementScreen(window, source.Window.Screens) == screen).ToArray();
            if (images.Length < 2 || images.Any(window => !window.CanArrangeSelection)) return;
            PixelRect[] bounds = [.. images.Select(window => window.SelectionLayoutBounds)];
            var original = images.Select(window => window.Window.Position).ToArray();
            PixelPoint[] positions;
            switch (arrangement)
            {
                case PinnedImageArrangement.DistributeHorizontal:
                case PinnedImageArrangement.DistributeVertical:
                {
                    if (images.Length < 3)
                    {
                        source.Presentation.ShowMessage(UiText.Get("PinnedSelectionNeedsThree"));
                        return;
                    }

                    var horizontal = arrangement == PinnedImageArrangement.DistributeHorizontal;
                    if (PinnedReferenceSelectionLayout.DistributionSpacing(bounds, horizontal) < 0 &&
                        !Preferences.SkipNegativeSpacingConfirmation)
                    {
                        var dialog = new PinnedSelectionSpacingDialog();
                        var proceed = await dialog.ShowDialog<bool>(source.Window);
                        if (dialog.DoNotAskAgain)
                        {
                            Preferences = Preferences with { SkipNegativeSpacingConfirmation = true };
                            PreferencesChanged?.Invoke(Preferences);
                        }

                        if (!proceed || !source.CanArrangeSelection) return;
                    }

                    positions = PinnedReferenceSelectionLayout.Distribute(bounds, horizontal);
                    break;
                }
                case PinnedImageArrangement.Compact:
                {
                    var available = screen.WorkingArea;
                    if (!PinnedCompactLayout.TryArrangeCompact(bounds,
                            new(available.X, available.Y, available.Width, available.Height), out positions))
                    {
                        await new PinnedSelectionSpaceDialog().ShowDialog(source.Window);
                        return;
                    }

                    break;
                }
                case PinnedImageArrangement.AlignTop:
                case PinnedImageArrangement.AlignMiddle:
                case PinnedImageArrangement.AlignBottom:
                case PinnedImageArrangement.AlignLeft:
                case PinnedImageArrangement.AlignRight:
                {
                    positions = PinnedReferenceSelectionLayout.Align(bounds, Array.IndexOf(images, source),
                        arrangement);
                    break;
                }
                case PinnedImageArrangement.AlignCenter:
                case PinnedImageArrangement.CompactHorizontal:
                case PinnedImageArrangement.CompactVertical:
                default:
                {
                    positions = PinnedImageLayoutEngine.Arrange(bounds, arrangement, screen.WorkingArea.Width);
                    break;
                }
            }

            try
            {
                for (var index = 0; index < images.Length; index++)
                {
                    if (!images[index].CanArrangeSelection) continue;
                    images[index].Window.Position = new(original[index].X + positions[index].X - bounds[index].X,
                        original[index].Y + positions[index].Y - bounds[index].Y);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                for (var index = 0; index < images.Length; index++)
                {
                    if (!images[index].CanArrangeSelection) continue;
                    try
                    {
                        images[index].Window.Position = original[index];
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }

                if (errors.Count > 1) throw new AggregateException(errors);
                throw;
            }

            ScheduleSave();
        }

        private static Screen? ArrangementScreen(IPinnedWindow window,
            Screens screens)
        {
            var bounds = window.SelectionLayoutBounds;
            return screens.ScreenFromPoint(new((bounds.X + bounds.Right - 1) / 2,
                (bounds.Y + bounds.Bottom - 1) / 2)) ?? screens.Primary;
        }
    }
}
