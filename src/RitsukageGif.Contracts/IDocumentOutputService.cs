namespace RitsukageGif.Contracts
{
    public interface IDocumentOutputService
    {
        Task<DocumentOutputInfo> SaveAsync(IReadOnlyList<string> images, string output,
            DocumentOutputOptions options, CancellationToken token = default);

        // ReSharper disable once UnusedMemberInSuper.Global
        Task<DocumentOutputInfo> SaveImageAsync(ReadOnlyMemory<byte> image, string output,
            DocumentOutputOptions options, CancellationToken token = default);
    }
}
