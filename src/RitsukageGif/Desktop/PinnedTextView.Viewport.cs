using Avalonia;
using Avalonia.Data;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedTextView
    {
        internal void DrawSceneViewport(DrawingContext context, Rect sourceViewport, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var foreground = SelectionLength > 0 && TextArea.SelectionForeground is not null
                ? TextArea.SetValue(TextArea.SelectionForegroundProperty, null,
                    BindingPriority.Animation)
                : null;
            var view = TextArea.TextView;
            view.EnsureVisualLines();
            var viewport = sourceViewport.Intersect(new(new(view.ScrollOffset.X, view.ScrollOffset.Y),
                view.Bounds.Size));
            using var clip = context.PushClip(viewport);
            foreach (var visual in view.VisualLines)
            {
                token.ThrowIfCancellationRequested();
                if (visual.VisualTop + visual.Height < viewport.Top) continue;
                if (visual.VisualTop > viewport.Bottom) break;
                var first = visual.GetTextLineByVisualYPosition(viewport.Top);
                var lines = visual.TextLines;
                for (var index = lines.IndexOf(first); index < lines.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var line = lines[index];
                    var top = visual.GetTextLineVisualYPosition(line, VisualYPosition.Baseline) - line.Baseline;
                    if (top > viewport.Bottom) break;
                    if (top + line.Height >= viewport.Top) line.Draw(context, new(0, top));
                }
            }
        }
    }
}
