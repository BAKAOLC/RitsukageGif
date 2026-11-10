using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportFrameTarget(byte[] output) : IPinnedViewportTarget
    {
        public void Write(int pixel, PinnedViewportSample sample, double opacity)
        {
            var bytes = output.AsSpan(pixel * 8);
            BinaryPrimitives.WriteHalfLittleEndian(bytes, (Half)(sample.Red * opacity));
            BinaryPrimitives.WriteHalfLittleEndian(bytes[2..], (Half)(sample.Green * opacity));
            BinaryPrimitives.WriteHalfLittleEndian(bytes[4..], (Half)(sample.Blue * opacity));
            BinaryPrimitives.WriteHalfLittleEndian(bytes[6..], (Half)(sample.Alpha * opacity));
        }
    }
}
