using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private async Task<RenderTargetBitmap> RenderDocumentSceneBaseAsync(CancellationToken token,
            double? scaling = null)
        {
            token.ThrowIfCancellationRequested();
            EnsureDocumentSceneLayout();
            var scale = scaling ?? RenderScaling;
            switch (Document)
            {
                case PinnedHtmlDocument html when !IgnoreFormat:
                    return await PinnedRichRender.RenderAsync(PinnedHtmlView.Parse(html),
                        _htmlResources ??= PinnedHtmlView.CreateResources(),
                        ((PinnedRichView)_textView!).FormattedWidth, scale, AppTheme.Surface, AppTheme.Ink, token,
                        ((PinnedRichView)_textView).SceneImageSizes);
                case PinnedFilesDocument:
                    return await RenderFileSceneAsync(scale, token);
                case PinnedFormulaDocument:
                {
                    var data = _formulaPngTask is not null ? await _formulaPngTask.WaitAsync(token) : null;
                    token.ThrowIfCancellationRequested();
                    if (data is null) throw new InvalidOperationException("The formula cannot be rendered.");
                    using var stream = new MemoryStream(data, false);
                    using var source = new Bitmap(stream);
                    var result = CreateDocumentSceneBitmap(source.Size, scale);
                    using var context = result.CreateDrawingContext();
                    context.FillRectangle(Brushes.White, new(default, source.Size));
                    context.DrawImage(source, new(source.Size));
                    return result;
                }
                case PinnedColorDocument:
                {
                    var output = CreateDocumentSceneBitmap(_surface.Child!.Bounds.Size, scale);
                    output.Render(_surface.Child);
                    return output;
                }
                default:
                    return RenderTextScene(scale, token);
            }
        }

        private RenderTargetBitmap RenderTextScene(double scale, CancellationToken token)
        {
            var view = CreateDocumentSceneTextView(out var width);
            try
            {
                var textView = view.TextArea.TextView;
                var height = 0d;
                var drawing = new DrawingGroup();
                using (var context = drawing.Open())
                    foreach (var line in view.Document.Lines)
                    {
                        token.ThrowIfCancellationRequested();
                        var visual = textView.GetOrConstructVisualLine(line);
                        foreach (var textLine in visual.TextLines)
                        {
                            textLine.Draw(context, new(0, height));
                            height += textLine.Height;
                        }

                        CheckDocumentSceneSize(new(width, Math.Max(1, height)), scale);
                    }

                var size = new Size(width, Math.Max(1, height));
                var output = CreateDocumentSceneBitmap(size, scale);
                using (var context = output.CreateDrawingContext())
                {
                    context.FillRectangle(AppTheme.Surface, new(default, size));
                    drawing.Draw(context);
                }

                return output;
            }
            finally
            {
                view.ReleaseContent();
            }
        }

        private PinnedTextView CreateDocumentSceneTextView(out double width)
        {
            var original = (PinnedTextView)_textView!;
            width = Math.Max(1, original.TextArea.TextView.Bounds.Width);
            var view = new PinnedTextView(Document.PlainText)
            {
                FontFamily = original.FontFamily, FontSize = original.FontSize,
                FontStyle = original.FontStyle, FontWeight = original.FontWeight,
                Foreground = original.Foreground ?? AppTheme.Ink,
            };
            view.TextArea.Foreground = view.Foreground;
            view.Measure(new(width, 720));
            view.Arrange(new(0, 0, width, 720));
            view.UpdateLayout();
            var textView = view.TextArea.TextView;
            textView.SetValue(TextElement.ForegroundProperty, view.Foreground);
            textView.SetValue(TextElement.FontFamilyProperty, view.FontFamily);
            textView.SetValue(TextElement.FontSizeProperty, view.FontSize);
            textView.SetValue(TextElement.FontStyleProperty, view.FontStyle);
            textView.SetValue(TextElement.FontWeightProperty, view.FontWeight);
            ((ILogicalScrollable)textView).CanHorizontallyScroll = false;
            textView.Measure(new(width, 720));
            textView.Arrange(new(0, 0, width, 720));
            return view;
        }

        private static RenderTargetBitmap CreateDocumentSceneBitmap(Size size, double scale)
        {
            CheckDocumentSceneSize(size, scale);
            return new(new(Math.Max(1, (int)Math.Ceiling(size.Width * scale)),
                    Math.Max(1, (int)Math.Ceiling(size.Height * scale))),
                new(96 * scale, 96 * scale));
        }

        private static void CheckDocumentSceneSize(Size size, double scale)
        {
            var width = Math.Max(1, Math.Ceiling(size.Width * scale));
            var height = Math.Max(1, Math.Ceiling(size.Height * scale));
            if (width * height > 32L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("PinnedDocumentImageTooLarge"));
        }
    }
}
