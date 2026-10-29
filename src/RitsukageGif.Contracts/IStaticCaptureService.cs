namespace RitsukageGif.Contracts
{
    public interface IStaticCaptureService
    {
        Task<MediaInfo> CaptureAsync(string output, StaticCaptureOptions options, CancellationToken token = default);

        Task<IReadOnlyList<MediaInfo>> CaptureOutputsAsync(IReadOnlyList<StaticCaptureOutput> outputs,
            StaticCaptureOptions options, CancellationToken token = default)
        {
            return Task.FromException<IReadOnlyList<MediaInfo>>(new NotSupportedException());
        }
    }
}
