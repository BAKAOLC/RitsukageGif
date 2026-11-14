namespace RitsukageGif.Contracts
{
    public interface IPinAutomationService
    {
        Task<IReadOnlyList<PinInfo>> ListAsync(CancellationToken token = default);
        Task<PinInfo?> GetAsync(string id, CancellationToken token = default);
        Task FocusAsync(string id, CancellationToken token = default);
        Task<IReadOnlyList<string>> FilesAsync(string id, CancellationToken token = default);
        Task<PinInfo> PinFileAsync(string path, CancellationToken token = default);
        Task<PinInfo> SetAsync(string id, PinUpdate update, CancellationToken token = default);
        Task<PinInfo> FitAsync(string id, int width, int height, CancellationToken token = default);
        Task CloseAsync(string id, bool destroy = false, CancellationToken token = default);
        Task<PinInfo?> RestoreAsync(string? id = null, CancellationToken token = default);
        Task<IReadOnlyList<PinClosedInfo>> ClosedAsync(CancellationToken token = default);
        Task<IReadOnlyList<PinGroupInfo>> GroupsAsync(CancellationToken token = default);
        Task<PinGroupInfo> CreateGroupAsync(string name, string? color = null, CancellationToken token = default);

        Task<PinGroupInfo> SetGroupAsync(string id, string? name = null, string? color = null,
            CancellationToken token = default);

        Task ActivateGroupAsync(string id, CancellationToken token = default);
        Task RemoveGroupAsync(string id, string? moveTo = null, CancellationToken token = default);
        Task<PinInfo> CreateImageAsync(ReadOnlyMemory<byte> image, CancellationToken token = default);
        Task<PinInfo> CreateTextAsync(string text, CancellationToken token = default);
        Task<PinInfo> CreateHtmlAsync(string html, string? text = null, CancellationToken token = default);
        Task<PinInfo> CreateColorAsync(string color, CancellationToken token = default);
        Task<PinInfo> DuplicateAsync(string id, CancellationToken token = default);

        Task<PinSnapshot> SnapshotAsync(string id, PinOutputView view = PinOutputView.Preferred,
            OutputColorMode color = OutputColorMode.Sdr, CancellationToken token = default);

        Task<IReadOnlyList<string>> SaveAsync(string id, string path, PinOutputView view = PinOutputView.Preferred,
            OutputColorMode color = OutputColorMode.Sdr, CancellationToken token = default);

        Task CopyAsync(string id, PinCopyKind kind = PinCopyKind.Image,
            PinOutputView view = PinOutputView.Preferred, CancellationToken token = default);

        Task<bool> TranslationAsync(string id, bool? enabled = null, CancellationToken token = default);
        Task<string?> TextAsync(string id, CancellationToken token = default);
        Task<string?> HtmlAsync(string id, CancellationToken token = default);
        Task<PinInfo> TransformAsync(string id, PinTransform transform, CancellationToken token = default);
        Task<PinInfo> AdjustBrightnessAsync(string id, double delta, CancellationToken token = default);
        Task<PinInfo> ResetProcessingAsync(string id, CancellationToken token = default);

        Task<IReadOnlyList<PinInfo>> ArrangeAsync(IReadOnlyList<string> ids, PinArrangement arrangement,
            bool allowNegativeSpacing = false, CancellationToken token = default);

        Task<ReadOnlyMemory<byte>> OriginalImageAsync(string id, CancellationToken token = default);

        Task<PinPlaybackState> PlaybackAsync(string id, bool? paused = null, double? speed = null,
            CancellationToken token = default);

        Task<PinPlaybackState> StepAsync(string id, int direction, CancellationToken token = default);
        Task<PinInfo> CreateFilesAsync(IReadOnlyList<string> paths, CancellationToken token = default);
        Task<PinInfo> CreateFormulaAsync(string latex, CancellationToken token = default);
        Task<PinInfo> SetThumbnailRegionAsync(string id, PixelRect region, CancellationToken token = default);
    }
}
