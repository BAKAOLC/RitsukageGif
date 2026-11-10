using Avalonia;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedRichView
    {
        internal void DrawSceneViewport(DrawingContext context, Rect sourceViewport, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var viewport = sourceViewport.Intersect(VisibleBounds);
            using var clip = context.PushClip(viewport);
            _layout!.Draw(context, viewport);
        }
    }
}
