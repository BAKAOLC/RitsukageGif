namespace RitsukageGif.Scripting
{
    public sealed record PresetContext(
        PresetWindowState? Window,
        bool Recording,
        bool Busy,
        double Elapsed,
        double RecordingElapsed,
        IReadOnlyDictionary<string, double> Constants,
        double PercentBasis = 0,
        IReadOnlyDictionary<string, object>? Variables = null,
        IReadOnlyList<string>? Outputs = null,
        IReadOnlyList<PresetRecordingStreamStatus>? Streams = null);
}
