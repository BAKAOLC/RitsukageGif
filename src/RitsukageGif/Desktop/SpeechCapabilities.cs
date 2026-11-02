namespace RitsukageGif.Desktop
{
    internal sealed record SpeechCapabilities(
        bool Available,
        SpeechVoice[] Voices,
        string? DefaultVoiceId = null,
        SpeechUnavailableReason Reason = SpeechUnavailableReason.None,
        string? Detail = null);
}
