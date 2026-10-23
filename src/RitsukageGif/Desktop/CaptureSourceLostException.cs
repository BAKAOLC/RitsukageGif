namespace RitsukageGif.Desktop
{
    internal sealed class CaptureSourceLostException(Exception? inner = null)
        : IOException(UiText.Get("CaptureSourceLost"), inner);
}
