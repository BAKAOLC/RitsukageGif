using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        private Size MeasureDocumentScene()
        {
            if (Document is PinnedColorDocument) return _surface.Child!.Bounds.Size;
            if (_textView is PinnedRichView rich) return rich.SceneSize;
            switch (Document)
            {
                case PinnedFilesDocument:
                    return WithFileScene(static list => list.Bounds.Size, _lifetime.Token);
                case PinnedFormulaDocument:
                    return _documentBitmap?.Size ?? _surface.Child!.Bounds.Size;
            }

            var view = CreateDocumentSceneTextView(out var width);
            try
            {
                var height = 0d;
                foreach (var line in view.Document.Lines)
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    var visual = view.TextArea.TextView.GetOrConstructVisualLine(line);
                    foreach (var textLine in visual.TextLines) height += textLine.Height;
                }

                return new(width, Math.Max(1, height));
            }
            finally
            {
                view.ReleaseContent();
            }
        }
    }
}
