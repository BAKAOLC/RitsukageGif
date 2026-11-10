using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private readonly ImageTextMarkup _textMarkup = new();
        private ScreenshotAnnotationSession? _textMarkupSession;
        private bool _textSelectionMarkPreview;

        private bool HasTextMarkup => _activeTranslatedRaster is null && CanSelectImageText && !_textMarkup.IsEmpty;

        private bool ToggleTextMarkup(ImageTextMarkKind kind, Color color)
        {
            var selected = _textMarkup.Toggle(_textSelection, kind, color);
            _textSelectionMarkPreview = _textMarkup.ContainsAny(_textSelection);
            RefreshTextMarkup();
            return selected;
        }

        private void RecolorTextMarkup(ImageTextMarkKind kind, Color color)
        {
            _textMarkup.Set(_textSelection, kind, color);
            _textSelectionMarkPreview = true;
            RefreshTextMarkup();
        }

        private void RemoveTextMarkup(ImageTextMarkKind kind)
        {
            _textMarkup.Set(_textSelection, kind, null);
            _textSelectionMarkPreview = _textMarkup.ContainsAny(_textSelection);
            RefreshTextMarkup();
        }

        private void RefreshTextMarkup()
        {
            var region = new PixelRect(0, 0, (int)_bitmap.Size.Width, (int)_bitmap.Size.Height);
            _textMarkupSession ??= new() { CanScroll = false };
            _textMarkupSession.NotifySelection(region);
            _textMarkupSession.SetTextMarkup(_textMarkup, OriginalCaptureScaling);
            if (_annotationScene is { } scene) scene.Underlay = HasTextMarkup ? _textMarkupSession : null;
            ShowTextSelection();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearTextMarkup()
        {
            _textMarkup.Restore([]);
            _textSelectionMarkPreview = false;
            _textMarkupSession?.Dispose();
            _textMarkupSession = null;
            if (_annotationScene is { } scene) scene.Underlay = null;
            if (_textSelectionSurface is { } surface)
            {
                surface.IsVisible = HasTextSelection;
                surface.InvalidateVisual();
                InvalidateInputUi();
            }
        }

        private async Task<(byte[] Png, CapturedFrame? Hdr)> ComposeTextMarkupAsync(byte[] png, CapturedFrame? hdr,
            OutputColorMode mode, CancellationToken token)
        {
            if (!HasTextMarkup || _textMarkupSession is not { HasAnnotations: true } marks) return (png, hdr);
            png = await marks.ComposeAsync(marks.Selection, png, token);
            if (mode != OutputColorMode.Sdr && hdr is not null)
                hdr = await marks.ComposeHdrAsync(marks.Selection, hdr, token: token);
            return (png, hdr);
        }

        private void RenderTextMarkup(DrawingContext context, double scale, PixelRect region)
        {
            if (_annotationScene is { AnnotationsVisible: true } || _hdrPreview is { IsVisible: true }) return;
            using var transform = context.PushTransform(new(scale, 0, 0, scale,
                -region.X * scale, -region.Y * scale));
            if (HasTextMarkup) _textMarkupSession?.Render(context);
        }
    }
}
