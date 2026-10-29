namespace RitsukageGif.Contracts
{
    public interface IStaticImageService
    {
        // Public contract for callers that supply their own image service.
        // ReSharper disable once UnusedMemberInSuper.Global
        Task<MediaInfo> SaveAsync(ReadOnlyMemory<byte> encodedImage, string output, StaticImageSaveOptions options,
            CancellationToken token = default);

        // Public contract for raw capture buffers as well as encoded images.
        // ReSharper disable once UnusedMemberInSuper.Global
        Task<MediaInfo> SavePixelsAsync(ReadOnlyMemory<byte> bgra, int width, int height, string output,
            StaticImageSaveOptions options, CancellationToken token = default);
    }
}
