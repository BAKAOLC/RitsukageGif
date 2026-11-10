namespace RitsukageGif.Desktop
{
    internal sealed record RecordingAnnotationLayerSnapshot(
        ScreenshotAnnotation Annotation,
        IReadOnlyList<RecordingAnnotationTile> Tiles);
}
