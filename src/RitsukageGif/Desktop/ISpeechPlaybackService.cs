namespace RitsukageGif.Desktop
{
    internal interface ISpeechPlaybackService : IAsyncDisposable
    {
        Task<SpeechCapabilities> GetCapabilitiesAsync(CancellationToken token = default);
        Task SpeakAsync(string text, string? language = null, CancellationToken token = default);
        Task StopAsync();
    }
}
