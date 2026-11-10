using Avalonia;
using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private Task<IReadOnlyList<(Bitmap Image, Rect Bounds)>> CreateSourcePreviewAsync(
            ScreenshotAnnotation annotation, ScreenshotAnnotation[] preceding,
            (PixelRect Region, byte[] Pixels)[] frames, CancellationToken token)
        {
            return annotation.Tool switch
            {
                ScreenshotTool.Magnifier => Task.FromResult<IReadOnlyList<(Bitmap Image, Rect Bounds)>>([]),
                ScreenshotTool.Eraser => CreateMosaicAsync(PixelEffectBounds(annotation).Intersect(new(
                    AnnotationBounds.X,
                    AnnotationBounds.Y, AnnotationBounds.Width, AnnotationBounds.Height)), 1, token, frames),
                _ => CreateSourcePixelPreviewAsync(annotation, preceding, frames, token),
            };
        }

        private async Task<IReadOnlyList<(Bitmap Image, Rect Bounds)>> CreateSourcePixelPreviewAsync(
            ScreenshotAnnotation annotation, ScreenshotAnnotation[] preceding,
            (PixelRect Region, byte[] Pixels)[] frames, CancellationToken token)
        {
            var sampling = preceding.ToArray();
            List<ScreenshotAnnotation> temporary = [];
            try
            {
                for (var index = 0; index < sampling.Length; index++)
                {
                    if (sampling[index].Tool != ScreenshotTool.Eraser) continue;
                    var copy = CloneAnnotation(sampling[index]);
                    temporary.Add(copy);
                    copy.ImageTiles = await CreateSourcePreviewAsync(copy, [], frames, token);
                    sampling[index] = copy;
                }

                return await CreatePixelPreviewAsync(annotation, sampling, token, frames);
            }
            finally
            {
                foreach (var copy in temporary) copy.Dispose();
            }
        }

        private IEnumerable<(ScreenshotAnnotation Annotation, ScreenshotAnnotation[] Preceding)> SourcePreviewLayers()
        {
            HashSet<ScreenshotAnnotation> visited = [];
            foreach (var state in new[] { _annotations.ToArray() }.Concat(_undo).Concat(_redo))
                for (var index = 0; index < state.Length; index++)
                {
                    var annotation = state[index];
                    if ((IsPixelTool(annotation.Tool) || annotation.Tool == ScreenshotTool.Magnifier) &&
                        visited.Add(annotation))
                        yield return (annotation, state[..index]);
                }

            foreach (var annotation in _retained)
                if ((IsPixelTool(annotation.Tool) || annotation.Tool == ScreenshotTool.Magnifier) &&
                    visited.Add(annotation))
                    yield return (annotation, []);
        }
    }
}
