namespace RitsukageGif.Desktop
{
    internal sealed record AudioCaptureDevice(string Id, string Name, bool Available = true, bool IsDefault = false);
}
