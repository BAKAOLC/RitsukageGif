using System.Buffers.Binary;

namespace RitsukageGif.Desktop
{
    internal readonly struct PinnedViewportPngTarget(byte[] output, byte depth, bool gray) : IPinnedViewportTarget
    {
        public void Write(int pixel, PinnedViewportSample sample, double opacity)
        {
            var channels = gray ? 2 : 4;
            var alpha = sample.Alpha * opacity;
            var divisor = alpha > 0 ? sample.Alpha : 0;
            WriteComponent(pixel * channels, divisor > 0 ? sample.Red / divisor : 0);
            if (!gray)
            {
                WriteComponent(pixel * channels + 1, divisor > 0 ? sample.Green / divisor : 0);
                WriteComponent(pixel * channels + 2, divisor > 0 ? sample.Blue / divisor : 0);
            }

            WriteComponent(pixel * channels + channels - 1, alpha);
        }

        private void WriteComponent(int component, double value)
        {
            var maximum = depth == 16 ? 65535 : 255;
            var sample = (ushort)Math.Clamp(Math.Round(value * maximum), 0, maximum);
            if (depth == 16) BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(component * 2), sample);
            else output[component] = (byte)sample;
        }
    }
}
