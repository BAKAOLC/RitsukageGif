using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PngColorTransform
    {
        public static PngRaster NormalizeAnnotationRaster(PngRaster raster, CancellationToken token)
        {
            var expanded = Png16PixelProcessor.ExpandRgba(raster, token);
            if (!raster.Metadata.Any(chunk => chunk.Type == "iCCP" ||
                                              IsRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                                              IsRawProfileChunk(chunk.Type, chunk.Data, "icm")))
                return expanded;
            var profile = ReadProfile(raster.Metadata, token);
            if (profile.Length < 20 || !profile.AsSpan(16, 4).SequenceEqual("GRAY"u8)) return expanded;
            if (raster.ColorType is not (0 or 4))
                throw new InvalidDataException("Grayscale ICC requires grayscale image samples.");
            var rgb = CreateGrayscaleRgbProfile(profile, token);
            using var encoded = new MemoryStream();
            encoded.Write("Expanded grayscale RGB\0\0"u8);
            using (var compressor = new ZLibStream(encoded, CompressionLevel.Optimal, true)) compressor.Write(rgb);
            var metadata = expanded.Metadata.Where(chunk =>
                    chunk.Type is not ("iCCP" or "sRGB" or "gAMA" or "cHRM" or "cICP") &&
                    !IsRawProfileChunk(chunk.Type, chunk.Data, "icc") &&
                    !IsRawProfileChunk(chunk.Type, chunk.Data, "icm"))
                .ToList();
            metadata.Add(("iCCP", encoded.ToArray()));
            return expanded with { Metadata = metadata };
        }

        private static byte[] CreateGrayscaleRgbProfile(byte[] gray, CancellationToken token)
        {
            if (gray.Length < 132 || !gray.AsSpan(20, 4).SequenceEqual("XYZ "u8) ||
                !gray.AsSpan(36, 4).SequenceEqual("acsp"u8))
                throw new InvalidDataException("Invalid grayscale ICC profile.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(gray);
            var count = BinaryPrimitives.ReadUInt32BigEndian(gray.AsSpan(128));
            if (length > gray.Length || length < 132 || count > (length - 132) / 12)
                throw new InvalidDataException("Invalid grayscale ICC tag table.");
            if (!gray.AsSpan(68, 12).SequenceEqual((ReadOnlySpan<byte>)[0, 0, 246, 214, 0, 1, 0, 0, 0, 0, 211, 45]))
                throw new NotSupportedException("Unsupported grayscale ICC connection-space illuminant.");
            var ranges = new List<(uint Offset, uint Size)>();
            byte[]? curve = null;
            for (var i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = gray.AsSpan(132 + i * 12, 12);
                var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
                var size = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
                if (offset < 132 + count * 12 || offset % 4 != 0 || offset > length || size < 8 ||
                    size > length - offset)
                    throw new InvalidDataException("Invalid grayscale ICC tag.");
                ranges.Add((offset, size));
                if (entry[..3].SequenceEqual("A2B"u8) || entry[..3].SequenceEqual("B2A"u8) ||
                    entry[..3].SequenceEqual("D2B"u8) || entry[..3].SequenceEqual("B2D"u8))
                    throw new NotSupportedException("Lookup-table grayscale ICC requires a general color transform.");
                if (!entry[..4].SequenceEqual("kTRC"u8)) continue;
                if (curve is not null || size < 12 || size > 4 * 1024 * 1024)
                    throw new InvalidDataException("Invalid grayscale ICC tone curve.");
                curve = [.. gray.AsSpan((int)offset, (int)size)];
            }

            ranges.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            for (var i = 1; i < ranges.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var previous = ranges[i - 1];
                var current = ranges[i];
                if (current.Offset < previous.Offset + previous.Size && current != previous)
                    throw new InvalidDataException("Overlapping grayscale ICC tags.");
            }

            if (curve is null ||
                !(curve.AsSpan(0, 4).SequenceEqual("curv"u8) || curve.AsSpan(0, 4).SequenceEqual("para"u8)))
                throw new NotSupportedException("Unsupported grayscale ICC tone curve.");
            if (curve.AsSpan(0, 4).SequenceEqual("curv"u8))
            {
                var samples = BinaryPrimitives.ReadUInt32BigEndian(curve.AsSpan(8));
                if (samples > (curve.Length - 12) / 2) throw new InvalidDataException("Truncated grayscale ICC curve.");
            }
            else
            {
                var function = BinaryPrimitives.ReadUInt16BigEndian(curve.AsSpan(8));
                ReadOnlySpan<int> parameters = [1, 3, 4, 5, 7];
                if (function >= parameters.Length || curve.Length < 12 + parameters[function] * 4)
                    throw new InvalidDataException("Invalid grayscale ICC parametric curve.");
            }

            var white = new byte[20];
            "XYZ "u8.CopyTo(white);
            gray.AsSpan(68, 12).CopyTo(white.AsSpan(8));
            var description = new byte[112];
            "desc"u8.CopyTo(description);
            BinaryPrimitives.WriteUInt32BigEndian(description.AsSpan(8), 23);
            "Expanded grayscale RGB\0"u8.CopyTo(description.AsSpan(12));
            (string Signature, byte[] Data)[] tags =
            [
                ("desc", description), ("cprt", [.. "text\0\0\0\0\0"u8]), ("wtpt", white),
                ("rXYZ", Axis(0)), ("gXYZ", Axis(1)), ("bXYZ", Axis(2)),
                ("rTRC", curve), ("gTRC", curve), ("bTRC", curve),
            ];
            var output = new byte[132 + tags.Length * 12 + tags.Sum(tag => (tag.Data.Length + 3) & ~3)];
            gray.AsSpan(0, 128).CopyTo(output);
            BinaryPrimitives.WriteUInt32BigEndian(output, (uint)output.Length);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), 0x02100000);
            "mntr"u8.CopyTo(output.AsSpan(12));
            "RGB "u8.CopyTo(output.AsSpan(16));
            output.AsSpan(84, 16).Clear();
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(128), (uint)tags.Length);
            var position = 132 + tags.Length * 12;
            for (var i = 0; i < tags.Length; i++)
            {
                var entry = output.AsSpan(132 + i * 12, 12);
                Encoding.ASCII.GetBytes(tags[i].Signature, entry[..4]);
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)position);
                BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
                tags[i].Data.CopyTo(output, position);
                position += (tags[i].Data.Length + 3) & ~3;
            }

            using var original = SKColorSpace.CreateIcc(gray);
            using var equivalent = SKColorSpace.CreateIcc(output);
            if (original is null || equivalent is null || !SKColorSpace.Equal(original, equivalent))
                throw new NotSupportedException("Grayscale ICC cannot be represented faithfully as RGB.");
            return output;

            byte[] Axis(int axis)
            {
                var tag = new byte[20];
                "XYZ "u8.CopyTo(tag);
                gray.AsSpan(68 + axis * 4, 4).CopyTo(tag.AsSpan(8 + axis * 4));
                return tag;
            }
        }
    }
}
