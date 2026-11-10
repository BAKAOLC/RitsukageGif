namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal bool FinishRecordingDrawing()
        {
            if (!IsDrawingPolyline) return false;
            FinishPolyline();
            return true;
        }
    }
}
