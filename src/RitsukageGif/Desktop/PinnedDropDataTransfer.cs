using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedDropDataTransfer(IReadOnlyList<IAsyncDataTransferItem> items) : IAsyncDataTransfer
    {
        public IReadOnlyList<DataFormat> Formats { get; } =
            Array.AsReadOnly<DataFormat>([.. items.SelectMany(item => item.Formats).Distinct()]);

        public IReadOnlyList<IAsyncDataTransferItem> Items { get; } =
            Array.AsReadOnly<IAsyncDataTransferItem>([.. items]);

        public void Dispose()
        {
        }
    }
}
