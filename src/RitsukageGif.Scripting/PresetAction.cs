namespace RitsukageGif.Scripting
{
    public sealed record PresetAction(
        string Kind,
        string? Target = null,
        double Seconds = 0,
        PresetExpression? Condition = null,
        IReadOnlyList<PresetAction>? Body = null,
        IReadOnlyList<PresetAction>? Otherwise = null,
        PresetRecordingSettings? Settings = null,
        IReadOnlyList<PresetExpression>? Arguments = null,
        int Line = 0,
        int Column = 0,
        IReadOnlyList<PresetRecordingOutput>? Outputs = null);
}
