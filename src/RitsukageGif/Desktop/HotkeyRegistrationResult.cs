namespace RitsukageGif.Desktop
{
    internal sealed record HotkeyRegistrationResult(
        string Shortcut,
        HotkeyRegistrationState State,
        string? Error = null)
    {
        internal string? TriggerDescription { get; init; }
    }
}
