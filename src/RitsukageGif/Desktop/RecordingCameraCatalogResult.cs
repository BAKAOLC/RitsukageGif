namespace RitsukageGif.Desktop
{
    internal sealed record RecordingCameraCatalogResult(IReadOnlyList<RecordingCameraDevice> Devices, string? Failure);
}
