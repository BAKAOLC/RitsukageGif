using Avalonia;
using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private bool DocumentSceneCoordinatesAvailable
        {
            get
            {
                var source = Document switch
                {
                    PinnedColorDocument => _surface.Child,
                    PinnedFormulaDocument when _documentBitmap is not null &&
                                               _surface.Child is Border { Child: Image image } => image,
                    _ => _textView switch
                    {
                        PinnedRichView rich => rich.SceneVisual,
                        PinnedTextView text => text.TextArea.TextView,
                        _ => _surface.Child,
                    },
                };
                return source is { Bounds: { Width: > 0, Height: > 0 } } &&
                       source.TranslatePoint(default, _content) is not null;
            }
        }

        private Point DocumentSceneViewportOrigin => DocumentScenePoint(default);

        private double DocumentSceneViewScale => _zoom * FormulaSceneStretch;

        private double FormulaSceneStretch =>
            Document is PinnedFormulaDocument && _documentBitmap is { } source &&
            _surface.Child is Border { Child: Image image }
                ? Math.Min(1, Math.Min(image.Bounds.Width / source.Size.Width,
                    image.Bounds.Height / source.Size.Height))
                : 1;

        private void EnsureDocumentSceneLayout()
        {
            var size = new Size(Width, Height);
            if (DocumentSceneCoordinatesAvailable &&
                Math.Abs(Presentation.Frame.Bounds.Width - size.Width) <= 1 / RenderScaling &&
                Math.Abs(Presentation.Frame.Bounds.Height - size.Height) <= 1 / RenderScaling) return;
            ApplyStyling();
            ApplyTemplate();
            Measure(size);
            Arrange(new(default, size));
            Presentation.Frame.Measure(size);
            Presentation.Frame.Arrange(new(default, size));
        }

        private Point DocumentScenePoint(Point pointInContent)
        {
            switch (Document)
            {
                case PinnedColorDocument:
                    return _content.TranslatePoint(pointInContent, _surface.Child!)!.Value;
                case PinnedFormulaDocument when _documentBitmap is { } source &&
                                                _surface.Child is Border { Child: Image image }:
                {
                    var point = _content.TranslatePoint(pointInContent, image)!.Value;
                    var scale = FormulaSceneStretch;
                    return new((point.X - (image.Bounds.Width - source.Size.Width * scale) / 2) / scale,
                        (point.Y - (image.Bounds.Height - source.Size.Height * scale) / 2) / scale);
                }
            }

            switch (_textView)
            {
                case PinnedRichView rich:
                    return _content.TranslatePoint(pointInContent, rich.SceneVisual)!.Value;
                case PinnedTextView text:
                    return _content.TranslatePoint(pointInContent, text.TextArea.TextView)!.Value + text.ScrollOffset;
            }

            if (_fileList is { } files)
                for (var index = 0; index < _fileSceneRows.Length; index++)
                    if (files.ContainerFromIndex(index) is { } container)
                    {
                        var point = _content.TranslatePoint(pointInContent, container)!.Value;
                        var row = _fileSceneRows[index];
                        return new(point.X + row.X, point.Y + row.Y);
                    }

            return _content.TranslatePoint(pointInContent, _surface.Child!)!.Value;
        }
    }
}
