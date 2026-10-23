namespace RitsukageGif.Scripting
{
    public sealed record PresetRecordingStreamStatus(
        string Name,
        string State,
        int Captured,
        int Encoded,
        double Elapsed,
        string Path,
        string? Error = null);
}
