namespace RitsukageGif.Desktop
{
    internal interface IPinnedInputRegionBackend : IDisposable
    {
        bool ClipsVisualOutput { get; }
        void Apply(PinnedInputRegion coverage);
        void Reset();
    }
}
