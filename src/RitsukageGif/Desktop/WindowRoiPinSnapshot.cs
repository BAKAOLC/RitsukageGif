namespace RitsukageGif.Desktop
{
    internal sealed class WindowRoiPinSnapshot(WindowRoiPinDescriptor descriptor, WindowRoiPinFrame frame) : IDisposable
    {
        internal WindowRoiPinDescriptor Descriptor { get; } = descriptor;
        internal WindowRoiPinFrame Frame { get; } = frame;
        internal long StorageBytes => Frame.StorageBytes;

        public void Dispose()
        {
            Frame.Dispose();
        }

        internal WindowRoiPinSnapshot Retain()
        {
            return new(Descriptor, Frame.Retain());
        }
    }
}
