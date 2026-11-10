namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        internal Task StopRecordingAsync()
        {
            return _controller.StopAsync();
        }
    }
}
