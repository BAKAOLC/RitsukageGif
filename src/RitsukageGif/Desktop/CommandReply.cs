namespace RitsukageGif.Desktop
{
    internal sealed record CommandReply(
        bool Success,
        string State,
        string Message = "",
        string? Output = null,
        PixelRect? Region = null,
        string? Preset = null,
        int Captured = 0,
        int Encoded = 0,
        string? PresetState = null,
        IReadOnlyList<string>? Outputs = null,
        string? PresetError = null,
        PresetDebugSnapshot? Debug = null,
        ScriptDiagnostic? Diagnostic = null,
        double CapturedFps = 0,
        double ElapsedSeconds = 0,
        string? ParameterSession = null,
        IReadOnlyList<PresetRecordingStreamStatus>? Streams = null,
        IReadOnlyList<InputChannelInfo>? Inputs = null,
        double? InputClock = null);
}
