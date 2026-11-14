using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace RitsukageGif.DragPreview
{
    public sealed class PreviewDataTransfer(
        DataTransfer transfer,
        Bitmap preview,
        PixelPoint hotSpot,
        double pixelScale)
        : IDataTransfer, IAsyncDataTransfer, IDragPreview
    {
        private int _disposed;

        public Bitmap Preview { get; } = preview;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try
            {
                ((IDisposable)transfer).Dispose();
            }
            finally
            {
                Preview.Dispose();
            }
        }
        public PixelPoint HotSpot { get; } = hotSpot;
        public double PixelScale { get; } = pixelScale;

        public IReadOnlyList<DataFormat> Formats => transfer.Formats;

        IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => ((IAsyncDataTransfer)transfer).Items;
        public IReadOnlyList<IDataTransferItem> Items => ((IDataTransfer)transfer).Items;
    }
}
