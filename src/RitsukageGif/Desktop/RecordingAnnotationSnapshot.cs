using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingAnnotationSnapshot(
        PixelSize nativeSize,
        IReadOnlyList<RecordingAnnotationLayerSnapshot> layers)
    {
        internal PixelSize NativeSize { get; } = nativeSize;
        internal IReadOnlyList<RecordingAnnotationLayerSnapshot> Layers { get; } = layers;

        internal bool NeedsOriginalSamples { get; } = layers.Any(layer =>
            layer.Annotation.Tool == ScreenshotTool.Eraser || layer.Annotation is
                { Tool: ScreenshotTool.Magnifier, Properties.MagnifierShowAnnotations: false });
    }
}
