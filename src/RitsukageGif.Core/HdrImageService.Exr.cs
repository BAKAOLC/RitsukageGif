using System.Buffers.Binary;
using System.Text;

namespace RitsukageGif.Core
{
    public static partial class HdrImageService
    {
        private static readonly string[] ExrChannelNames = ["A", "B", "G", "R"];
        private static readonly float[] ExrChromaticities = [.64f, .33f, .30f, .60f, .15f, .06f, .3127f, .3290f];

        private static void WriteExr(Stream stream, CapturedFrame frame, CancellationToken token)
        {
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write(20000630);
            writer.Write(2); // Single-part scanline OpenEXR, no optional format flags.
            Attribute("channels", "chlist", value =>
            {
                foreach (var name in ExrChannelNames)
                {
                    Text(value, name);
                    value.Write(1); // HALF.
                    value.Write(0); // pLinear and three reserved bytes.
                    value.Write(1);
                    value.Write(1);
                }

                value.Write((byte)0);
            });
            Attribute("compression", "compression", value => value.Write((byte)0));
            Attribute("dataWindow", "box2i", Box);
            Attribute("displayWindow", "box2i", Box);
            Attribute("lineOrder", "lineOrder", value => value.Write((byte)0));
            Attribute("pixelAspectRatio", "float", value => value.Write(1f));
            Attribute("screenWindowCenter", "v2f", value =>
            {
                value.Write(0f);
                value.Write(0f);
            });
            Attribute("screenWindowWidth", "float", value => value.Write(1f));
            Attribute("chromaticities", "chromaticities", value =>
            {
                foreach (var coordinate in ExrChromaticities)
                    value.Write(coordinate);
            });
            Attribute("whiteLuminance", "float", value => value.Write(FrameColorInfo.ScRgbReferenceWhiteNits));
            writer.Write((byte)0);
            var rowBytes = checked(frame.Width * 8);
            var firstBlock = checked(stream.Position + frame.Height * 8L);
            for (var y = 0; y < frame.Height; y++) writer.Write(firstBlock + y * (rowBytes + 8L));
            var row = new byte[rowBytes];
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var input = frame.Pixels.Span.Slice(y * frame.Stride, rowBytes);
                // EXR channels are alphabetically sorted and alpha-associated.
                for (var channel = 0; channel < 4; channel++)
                {
                    var inputChannel = 3 - channel;
                    for (var x = 0; x < frame.Width; x++)
                    {
                        var bits = BinaryPrimitives.ReadUInt16LittleEndian(input[(x * 8 + inputChannel * 2)..]);
                        if (channel == 0 && frame.AlphaMode == CaptureAlphaMode.Opaque)
                        {
                            bits = BitConverter.HalfToUInt16Bits((Half)1);
                        }
                        else if (channel != 0 && frame.AlphaMode == CaptureAlphaMode.Straight)
                        {
                            var alpha = BitConverter.UInt16BitsToHalf(
                                BinaryPrimitives.ReadUInt16LittleEndian(input[(x * 8 + 6)..]));
                            bits = BitConverter.HalfToUInt16Bits((Half)((float)BitConverter.UInt16BitsToHalf(bits) *
                                                                        (float)alpha));
                        }

                        BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan((channel * frame.Width + x) * 2), bits);
                    }
                }

                writer.Write(y);
                writer.Write(rowBytes);
                writer.Write(row);
            }

            return;

            void Box(BinaryWriter value)
            {
                value.Write(0);
                value.Write(0);
                value.Write(frame.Width - 1);
                value.Write(frame.Height - 1);
            }

            void Attribute(string name, string type, Action<BinaryWriter> write)
            {
                using var payload = new MemoryStream();
                using var value = new BinaryWriter(payload, Encoding.ASCII, true);
                write(value);
                Text(writer, name);
                Text(writer, type);
                writer.Write(checked((int)payload.Length));
                writer.Write(payload.GetBuffer().AsSpan(0, (int)payload.Length));
            }
        }

        private static void Text(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
            writer.Write((byte)0);
        }
    }
}
