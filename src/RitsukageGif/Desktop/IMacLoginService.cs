namespace RitsukageGif.Desktop
{
    internal interface IMacLoginService
    {
        StartupRegistrationStatus Status { get; }
        void SetEnabled(bool enabled);
    }
}
