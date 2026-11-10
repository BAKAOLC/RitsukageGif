namespace RitsukageGif.Contracts
{
    public interface IAnimationDocument : IAsyncDisposable
    {
        AnimationDocumentInfo Current { get; }
        bool IsClosed { get; }
        Task SetDelayAsync(IReadOnlyList<int> indices, int delayMs, CancellationToken token = default);
        Task SetIterationsAsync(uint iterations, CancellationToken token = default);
        Task DuplicateAsync(IReadOnlyList<int> indices, CancellationToken token = default);
        Task DeleteAsync(IReadOnlyList<int> indices, CancellationToken token = default);
        Task MoveAsync(int from, int to, CancellationToken token = default);
        Task AddBlankAsync(int insertIndex, int delayMs, string color, CancellationToken token = default);
        Task ImportAsync(string path, int insertIndex, CancellationToken token = default);
        Task UndoAsync(CancellationToken token = default);
        Task RedoAsync(CancellationToken token = default);
        Task TransformAsync(MediaProcessOptions options, CancellationToken token = default);

        Task ApplyEffectsAsync(IReadOnlyList<int> indices, IReadOnlyList<PixelEffect> effects,
            CancellationToken token = default);

        Task OverlayAsync(IReadOnlyList<int> indices, string path, int x, int y, CancellationToken token = default);
        Task ReplaceFramesAsync(IReadOnlyList<int> indices, byte[] image, CancellationToken token = default);
        Task ExportFrameAsync(int index, string path, CancellationToken token = default);

        Task<int[]> MoveSelectedAsync(IReadOnlyList<int> indices, int direction,
            CancellationToken token = default);

        Task ExportAsync(string path, MediaProcessOptions options, CancellationToken token = default);
    }
}
