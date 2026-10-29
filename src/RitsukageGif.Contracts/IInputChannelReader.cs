namespace RitsukageGif.Contracts
{
    public interface IInputChannelReader
    {
        IReadOnlyList<string> Names { get; }
        InputChannelData? Read(string name);

        Task<InputChannelData?> WaitAsync(string name, long afterVersion, double timeoutSeconds,
            CancellationToken token = default);
    }
}
