namespace RitsukageGif.Contracts
{
    public interface IImageCompositionService
    {
        Task<MediaInfo> ComposeAsync(string output, ImageCompositionOptions options, CancellationToken token = default);

        Task<MediaInfo> ContactSheetAsync(string input, string output, ImageContactSheetOptions options,
            CancellationToken token = default);
    }
}
