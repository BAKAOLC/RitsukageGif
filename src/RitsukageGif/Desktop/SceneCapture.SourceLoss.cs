namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCapture
    {
        private void ReportWindowSourceLoss(Layer layer, CancellationToken token)
        {
            if (layer.Definition is { Source: CaptureSourceKind.Window, Media: null })
            {
                WindowSourceLost?.Invoke();
                token.ThrowIfCancellationRequested();
            }
        }
    }
}
