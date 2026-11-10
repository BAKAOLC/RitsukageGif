using Avalonia;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedTextSelectionMenu? _textSelectionMenu;

        private void ShowTextSelectionMenu()
        {
            if (!HasTextSelection || _activeTranslatedRaster is not null || !CurrentPreferences.ShowTextPopupMenu ||
                _closed) return;
            if (_textSelectionMenu is null)
            {
                _textSelectionMenu = new(this, () => CurrentPreferences, ToggleTextMarkup, RecolorTextMarkup,
                    RemoveTextMarkup, () => _ = RunAsync(CopyTextSelectionFromMenuAsync), ClearTextSelection);
                _content.Children.Add(_textSelectionMenu);
            }

            var spans = ActiveTextSelection.SelectedSpans;
            var bounds = spans[0].Bounds;
            foreach (var span in spans[1..]) bounds = bounds.Union(span.Bounds);
            var region = DisplayRegion;
            var scale = CurrentPinZoom / RenderScaling;
            if (_image.TransformToVisual(this) is { } matrix)
                _textSelectionMenu.Open(new Rect((bounds.Left - region.X) * scale,
                        (bounds.Top - region.Y) * scale, bounds.Width * scale, bounds.Height * scale)
                    .TransformToAABB(matrix));
        }

        private async Task CopyTextSelectionFromMenuAsync()
        {
            var copy = CopySelectedTextAsync();
            ClearTextSelection();
            await copy;
        }
    }
}
