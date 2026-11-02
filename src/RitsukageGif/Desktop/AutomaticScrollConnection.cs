namespace RitsukageGif.Desktop
{
    internal sealed record AutomaticScrollConnection(
        IAutomaticScrollInput? Input,
        string? Reason = null,
        bool CanRequestAccess = false) : IDisposable
    {
        public void Dispose()
        {
            (Input as IDisposable)?.Dispose();
        }
    }
}
