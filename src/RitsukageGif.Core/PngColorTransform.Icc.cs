using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace RitsukageGif.Core
{
    public static partial class PngColorTransform
    {
        public static byte[] CreateRgbProfile(SKColorSpace space)
        {
            if (!space.GetNumericalTransferFunction(out var transfer) || !space.ToColorSpaceXyz(out var matrix))
                throw new NotSupportedException("The color space requires a non-parametric ICC profile.");
            var xyz = matrix.Values;
            var curve = new byte[40];
            "para"u8.CopyTo(curve);
            BinaryPrimitives.WriteUInt16BigEndian(curve.AsSpan(8), 4);
            float[] parameters = [transfer.G, transfer.A, transfer.B, transfer.C, transfer.D, transfer.E, transfer.F];
            for (var i = 0; i < parameters.Length; i++) Fixed(curve.AsSpan(12 + i * 4), parameters[i]);
            (string Signature, byte[] Data)[] tags =
            [
                ("desc", Text("RGB color space")), ("cprt", Text(" ")), ("wtpt", Xyz(.9642f, 1, .8249f)),
                ("rXYZ", Xyz(xyz[0], xyz[3], xyz[6])),
                ("gXYZ", Xyz(xyz[1], xyz[4], xyz[7])),
                ("bXYZ", Xyz(xyz[2], xyz[5], xyz[8])),
                ("rTRC", curve), ("gTRC", curve), ("bTRC", curve),
            ];
            var result = new byte[132 + tags.Length * 12 + tags.Sum(tag => (tag.Data.Length + 3) & ~3)];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), 0x04300000);
            "mntr"u8.CopyTo(result.AsSpan(12));
            "RGB "u8.CopyTo(result.AsSpan(16));
            "XYZ "u8.CopyTo(result.AsSpan(20));
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(24), 2000);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(26), 1);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(28), 1);
            "acsp"u8.CopyTo(result.AsSpan(36));
            Xyz(.9642f, 1, .8249f).AsSpan(8, 12).CopyTo(result.AsSpan(68));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(128), (uint)tags.Length);
            var position = 132 + tags.Length * 12;
            for (var i = 0; i < tags.Length; i++)
            {
                var entry = result.AsSpan(132 + i * 12, 12);
                Encoding.ASCII.GetBytes(tags[i].Signature, entry[..4]);
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)position);
                BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
                tags[i].Data.CopyTo(result, position);
                position += (tags[i].Data.Length + 3) & ~3;
            }

            return result;
        }

        private static byte[] Text(string value)
        {
            var text = Encoding.BigEndianUnicode.GetBytes(value);
            var result = new byte[28 + text.Length];
            "mluc"u8.CopyTo(result);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), 12);
            "enUS"u8.CopyTo(result.AsSpan(16));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20), (uint)text.Length);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(24), 28);
            text.CopyTo(result, 28);
            return result;
        }

        private static byte[] Xyz(float x, float y, float z)
        {
            var result = new byte[20];
            "XYZ "u8.CopyTo(result);
            Fixed(result.AsSpan(8), x);
            Fixed(result.AsSpan(12), y);
            Fixed(result.AsSpan(16), z);
            return result;
        }

        private static void Fixed(Span<byte> target, float value)
        {
            BinaryPrimitives.WriteInt32BigEndian(target, checked((int)Math.Round(value * 65536d)));
        }
    }
}
