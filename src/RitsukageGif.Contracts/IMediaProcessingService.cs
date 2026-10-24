namespace RitsukageGif.Contracts
{
    public interface IMediaProcessingService
    {
        Task<MediaInfo> InspectAsync(string path, CancellationToken token = default);

        Task<MediaInfo> ProcessAsync(string input, string output, MediaProcessOptions options,
            CancellationToken token = default);

        async Task<IReadOnlyList<MediaProcessResult>> ProcessManyAsync(string input,
            IReadOnlyList<MediaProcessOutput> outputs, CancellationToken token = default)
        {
            var results = new MediaProcessResult[outputs.Count];
            for (var index = 0; index < outputs.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var output = outputs[index];
                    results[index] = new(await ProcessAsync(input, output.Path, output.Options, token), null);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    results[index] = new(null, error);
                }
            }

            return results;
        }
    }
}
