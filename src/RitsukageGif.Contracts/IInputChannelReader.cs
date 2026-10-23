namespace RitsukageGif.Contracts
{
    public interface IInputChannelReader
    {
        InputChannelData? Read(string name);
        IReadOnlyList<string> Names { get; }

        Task<InputChannelData?> WaitAsync(string name, long afterVersion, double timeoutSeconds,
            CancellationToken token = default);
    }
}
