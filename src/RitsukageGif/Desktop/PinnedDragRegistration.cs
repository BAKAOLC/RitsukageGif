using System.Runtime.CompilerServices;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedDragRegistration : IDisposable
    {
        private static readonly ConditionalWeakTable<IDataTransfer, PinnedDragRegistration> Sources = new();
        private readonly IDataTransfer _transfer;

        private PinnedDragRegistration(IDataTransfer transfer, IPinnedWindow source, bool disposeSourceBitmap)
        {
            _transfer = transfer;
            Source = source;
            DisposeSourceBitmap = disposeSourceBitmap;
        }

        internal IPinnedWindow Source { get; }
        internal bool DisposeSourceBitmap { get; }

        public void Dispose()
        {
            Sources.Remove(_transfer);
        }

        internal static PinnedDragRegistration Register(IDataTransfer transfer, IPinnedWindow source,
            bool disposeSourceBitmap)
        {
            var registration = new PinnedDragRegistration(transfer, source, disposeSourceBitmap);
            Sources.Add(transfer, registration);
            return registration;
        }

        internal static PinnedDragRegistration? Find(IDataTransfer transfer)
        {
            return Sources.TryGetValue(transfer, out var source) ? source : null;
        }
    }
}
