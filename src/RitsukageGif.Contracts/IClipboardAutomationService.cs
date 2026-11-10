namespace RitsukageGif.Contracts
{
    public interface IClipboardAutomationService
    {
        Task<bool> IsAvailableAsync(CancellationToken token = default);
        Task<string?> ReadTextAsync(CancellationToken token = default);
        Task<string?> ReadHtmlAsync(CancellationToken token = default);
        Task<IReadOnlyList<string>> ReadFilesAsync(CancellationToken token = default);
        Task<byte[]?> ReadImageAsync(CancellationToken token = default);
        Task WriteTextAsync(string text, CancellationToken token = default);
        Task WriteHtmlAsync(string html, string? text = null, CancellationToken token = default);
        Task WriteFilesAsync(IReadOnlyList<string> paths, CancellationToken token = default);
        Task WriteImageAsync(ReadOnlyMemory<byte> png, CancellationToken token = default);
        Task ClearAsync(CancellationToken token = default);
    }
}
