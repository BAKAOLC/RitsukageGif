using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task<ScreenshotResult> CaptureFullTextAsync(CancellationToken token)
        {
            if (Document is PinnedHtmlDocument html && !IgnoreFormat)
            {
                var encoded = await PinnedRichRender.EncodeAsync(PinnedHtmlView.Parse(html),
                    _htmlResources ??= PinnedHtmlView.CreateResources(),
                    ((PinnedRichView)_textView!).FormattedWidth, RenderScaling, AppTheme.Surface, AppTheme.Ink, token);
                return new(encoded, ScreenshotOutputAction.Save)
                    { ColorMode = OutputColorMode.Sdr, CaptureScaling = RenderScaling };
            }

            var view = new PinnedTextView(Document.PlainText);
            try
            {
                var width = Math.Max(120, _surface.Bounds.Width - 20);
                view.Foreground = AppTheme.Ink;
                view.TextArea.Foreground = AppTheme.Ink;
                view.Measure(new(width, 720));
                view.Arrange(new(0, 0, width, 720));
                view.UpdateLayout();
                var textView = view.TextArea.TextView;
                textView.SetValue(TextElement.ForegroundProperty, AppTheme.Ink);
                textView.SetValue(TextElement.FontFamilyProperty, view.FontFamily);
                textView.SetValue(TextElement.FontSizeProperty, view.FontSize);
                ((ILogicalScrollable)textView).CanHorizontallyScroll = false;
                textView.Measure(new(width, 720));
                textView.Arrange(new(0, 0, width, 720));
                var pixelsWide = Math.Max(1, (int)Math.Ceiling(width * RenderScaling));
                var maximumHeight = 32d * 1024 * 1024 / pixelsWide / RenderScaling;
                var height = 4d;
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                {
                    foreach (var line in view.Document.Lines)
                    {
                        token.ThrowIfCancellationRequested();
                        var visual = view.TextArea.TextView.GetOrConstructVisualLine(line);
                        foreach (var textLine in visual.TextLines)
                        {
                            if (height + textLine.Height > maximumHeight)
                                throw new InvalidOperationException(UiText.Get("PinnedDocumentImageTooLarge"));
                            textLine.Draw(context, new(0, height));
                            height += textLine.Height;
                        }
                    }

                    context.DrawRectangle(Brushes.Transparent, null, new(0, 0, width, height));
                }

                var image = new Image { Source = new DrawingImage(drawing), Width = width, Height = height };
                image.Measure(new(width, height));
                image.Arrange(new(0, 0, width, height));
                using var bitmap = new RenderTargetBitmap(new(pixelsWide,
                        Math.Max(1, (int)Math.Ceiling(height * RenderScaling))),
                    new(96 * RenderScaling, 96 * RenderScaling));
                bitmap.Render(image);
                using var stream = new MemoryStream();
                await Task.Run(() => bitmap.Save(stream, PngBitmapEncoderOptions.Default), token);
                return new(stream.ToArray(), ScreenshotOutputAction.Save)
                    { ColorMode = OutputColorMode.Sdr, CaptureScaling = RenderScaling };
            }
            finally
            {
                view.ReleaseContent();
            }
        }
    }
}
