namespace RitsukageGif.Desktop
{
    internal sealed record RecordingCameraOptions
    {
        internal bool Enabled { get; init; }
        internal RecordingCameraSlot Primary { get; init; } = new();
        internal RecordingCameraSlot Secondary { get; init; } = new();
    }
}
