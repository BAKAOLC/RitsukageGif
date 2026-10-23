namespace RitsukageGif.Contracts
{
    public interface IMediaProcessingService
    {
        Task<MediaInfo> InspectAsync(string path, CancellationToken token = default);

        Task<MediaInfo> ProcessAsync(string input, string output, MediaProcessOptions options,
            CancellationToken token = default);
    }
}
