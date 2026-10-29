using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PngColorTransform
    {
        public static byte[] ConvertToSrgb(byte[] pixels, int width, int height, int depth, int colorType,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks, CancellationToken token)
        {
            return ConvertSamples(pixels, width, height, depth, colorType, chunks, token);
        }

        public static byte[] ConvertSamples(byte[] pixels, int width, int height, int depth, int colorType,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks, CancellationToken token,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)>? destinationChunks = null)
        {
            using var sourceSpace = CreateSourceSpace(chunks, token);
            using var targetSpace = destinationChunks is null
                ? SKColorSpace.CreateSrgb()
                : CreateSourceSpace(destinationChunks, token);
            var components = colorType switch
            {
                0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new InvalidDataException(),
            };
            var sourceComponentBytes = depth == 16 ? 2 : 1;
            var sourcePixelBytes = components * sourceComponentBytes;
            var targetComponentBytes = depth == 16 ? 2 : 1;
            var maximum = (1 << depth) - 1;
            var outputMaximum = depth == 16 ? 65535 : 255;
            var outputLength = (long)width * height * 4 * targetComponentBytes;
            if (outputLength > 512L * 1024 * 1024)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(pixels));
            var output = new byte[checked((int)outputLength)];
            var palette = chunks.FirstOrDefault(chunk => chunk.Type == "PLTE").Data;
            var transparency = chunks.FirstOrDefault(chunk => chunk.Type == "tRNS").Data;
            if ((colorType == 3 && (palette.IsEmpty || palette.Length % 3 != 0 || palette.Length > 768)) ||
                (!transparency.IsEmpty && ((colorType == 0 && transparency.Length != 2) ||
                                           (colorType == 2 && transparency.Length != 6))))
                throw new InvalidDataException();
            var tileWidth = Math.Min(width, 4096);
            var tileHeight = Math.Min(height, 16);
            var samples = new float[checked(tileWidth * tileHeight * 4)];
            var converted = new float[samples.Length];
            var inputHandle = GCHandle.Alloc(samples, GCHandleType.Pinned);
            try
            {
                var outputHandle = GCHandle.Alloc(converted, GCHandleType.Pinned);
                try
                {
                    for (var top = 0; top < height; top += tileHeight)
                    for (var left = 0; left < width; left += tileWidth)
                    {
                        token.ThrowIfCancellationRequested();
                        var columns = Math.Min(tileWidth, width - left);
                        var rows = Math.Min(tileHeight, height - top);
                        for (var y = 0; y < rows; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            for (var x = 0; x < columns; x++)
                            {
                                var pixel = pixels.AsSpan(((top + y) * width + left + x) * sourcePixelBytes,
                                    sourcePixelBytes);
                                var destination = samples.AsSpan((y * columns + x) * 4, 4);
                                if (colorType == 3)
                                {
                                    var index = pixel[0];
                                    if (index * 3 + 2 >= palette.Length) throw new InvalidDataException();
                                    destination[0] = palette.Span[index * 3] / 255f;
                                    destination[1] = palette.Span[index * 3 + 1] / 255f;
                                    destination[2] = palette.Span[index * 3 + 2] / 255f;
                                    destination[3] = index < transparency.Length ? transparency.Span[index] / 255f : 1;
                                }
                                else
                                {
                                    var red = ReadTransformPngComponent(pixel, 0, sourceComponentBytes);
                                    var green = components < 3
                                        ? red
                                        : ReadTransformPngComponent(pixel, 1, sourceComponentBytes);
                                    var blue = components < 3
                                        ? red
                                        : ReadTransformPngComponent(pixel, 2, sourceComponentBytes);
                                    var alpha = colorType is 4 or 6
                                        ? ReadTransformPngComponent(pixel, components - 1, sourceComponentBytes)
                                        : maximum;
                                    if (!transparency.IsEmpty &&
                                        ((colorType == 0 &&
                                          red == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span)) ||
                                         (colorType == 2 &&
                                          red == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span) &&
                                          green == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span[2..]) &&
                                          blue == BinaryPrimitives.ReadUInt16BigEndian(transparency.Span[4..]))))
                                        alpha = 0;
                                    destination[0] = (float)red / maximum;
                                    destination[1] = (float)green / maximum;
                                    destination[2] = (float)blue / maximum;
                                    destination[3] = (float)alpha / maximum;
                                }
                            }
                        }

                        var sourceInfo = new SKImageInfo(columns, rows, SKColorType.RgbaF32, SKAlphaType.Unpremul,
                            sourceSpace);
                        var targetInfo = new SKImageInfo(columns, rows, SKColorType.RgbaF32, SKAlphaType.Unpremul,
                            targetSpace);
                        using var pixmap = new SKPixmap(sourceInfo, inputHandle.AddrOfPinnedObject(),
                            sourceInfo.RowBytes);
                        if (!pixmap.ReadPixels(targetInfo, outputHandle.AddrOfPinnedObject(), targetInfo.RowBytes))
                            throw new InvalidDataException();
                        for (var y = 0; y < rows; y++)
                        {
                            token.ThrowIfCancellationRequested();
                            for (var x = 0; x < columns; x++)
                            {
                                var destination =
                                    output.AsSpan(((top + y) * width + left + x) * 4 * targetComponentBytes,
                                        4 * targetComponentBytes);
                                for (var component = 0; component < 4; component++)
                                {
                                    var value = converted[(y * columns + x) * 4 + component];
                                    if (!float.IsFinite(value)) throw new InvalidDataException();
                                    var encoded = (ushort)Math.Round(Math.Clamp((double)value, 0, 1) * outputMaximum);
                                    if (targetComponentBytes == 1) destination[component] = (byte)encoded;
                                    else BinaryPrimitives.WriteUInt16BigEndian(destination[(component * 2)..], encoded);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    outputHandle.Free();
                }
            }
            finally
            {
                inputHandle.Free();
            }

            return output;
        }

        private static byte[] ReadProfile(IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks,
            CancellationToken token)
        {
            var profiles = chunks.Where(chunk => chunk.Type == "iCCP").ToArray();
            if (profiles.Length > 1) throw new InvalidDataException();
            var rawProfiles = chunks.Where(chunk => IsRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                                                    IsRawProfileChunk(chunk.Type, chunk.Data, "icm"))
                .ToArray();
            byte[] profile;
            if (profiles.Length == 0)
            {
                if (rawProfiles.Length != 1) throw new InvalidDataException();
                profile = ReadTransformRawProfile(rawProfiles[0].Type, rawProfiles[0].Data, token);
            }
            else
            {
                profile = ReadTransformIccProfile(profiles[0].Data, token);
            }

            return profile;
        }

        private static byte[] ReadTransformIccProfile(ReadOnlyMemory<byte> profileData, CancellationToken token)
        {
            var nameEnd = profileData.Span.IndexOf((byte)0);
            if (nameEnd is < 1 or > 79 || nameEnd + 2 >= profileData.Length || profileData.Span[nameEnd + 1] != 0)
                throw new InvalidDataException();
            using var profileInput = new MemoryStream(profileData[(nameEnd + 2)..].ToArray(), false);
            using var inflate = new ZLibStream(profileInput, CompressionMode.Decompress);
            var size = new byte[4];
            inflate.ReadExactly(size);
            var profileLength = BinaryPrimitives.ReadUInt32BigEndian(size);
            if (profileLength is < 128 or > PngImageCodec.MaximumEncodedBytes) throw new InvalidDataException();
            var profile = new byte[checked((int)profileLength)];
            size.CopyTo(profile, 0);
            for (var offset = 4; offset < profile.Length; offset += 65536)
            {
                token.ThrowIfCancellationRequested();
                inflate.ReadExactly(profile.AsSpan(offset, Math.Min(65536, profile.Length - offset)));
            }

            if (inflate.ReadByte() != -1) throw new InvalidDataException();
            return profile;
        }

        private static int ReadTransformPngComponent(ReadOnlySpan<byte> pixels, int index, int componentBytes)
        {
            return componentBytes == 1 ? pixels[index] : BinaryPrimitives.ReadUInt16BigEndian(pixels[(index * 2)..]);
        }

        public static bool IsRawProfileChunk(string type, ReadOnlyMemory<byte> data, string profile)
        {
            if (type is not ("tEXt" or "zTXt" or "iTXt")) return false;
            var end = data.Span.IndexOf((byte)0);
            return end is >= 1 and <= 79 && Encoding.ASCII.GetString(data.Span[..end])
                .Equals("Raw profile type " + profile, StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] ReadTransformRawProfile(string type, ReadOnlyMemory<byte> data, CancellationToken token)
        {
            var offset = data.Span.IndexOf((byte)0) + 1;
            var compressed = type == "zTXt";
            switch (type)
            {
                case "zTXt":
                    if (offset >= data.Length || data.Span[offset++] != 0) throw new InvalidDataException();
                    break;
                case "iTXt":
                    if (offset + 2 > data.Length || data.Span[offset] > 1 || data.Span[offset + 1] != 0)
                        throw new InvalidDataException();
                    compressed = data.Span[offset] == 1;
                    offset += 2;
                    for (var index = 0; index < 2; index++)
                    {
                        var end = data.Span[offset..].IndexOf((byte)0);
                        if (end < 0) throw new InvalidDataException();
                        offset += end + 1;
                    }

                    break;
            }

            using var source = new MemoryStream(data[offset..].ToArray(), false);
            using var inflate = compressed ? new ZLibStream(source, CompressionMode.Decompress, true) : null;
            using var reader = new StreamReader(inflate is null ? source : inflate, Encoding.ASCII, false, 4096);
            var read = 0L;
            var limit = (long)PngImageCodec.MaximumEncodedBytes * 3 + 4096;
            var name = ReadToken(80);
            if (name is not ("icc" or "icm") ||
                !int.TryParse(ReadToken(16), NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
                size is < 128 or > PngImageCodec.MaximumEncodedBytes) throw new InvalidDataException();
            limit = (long)size * 3 + 4096;
            var profile = new byte[size];
            for (var index = 0; index < profile.Length; index++)
            {
                if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
                var high = ReadHex();
                var low = ReadHex();
                profile[index] = (byte)((high << 4) | low);
            }

            for (var value = Read(); value != -1; value = Read())
                if (!char.IsWhiteSpace((char)value))
                    throw new InvalidDataException();
            if (BinaryPrimitives.ReadUInt32BigEndian(profile) != size) throw new InvalidDataException();
            return profile;

            int Read()
            {
                if ((read & 65535) == 0) token.ThrowIfCancellationRequested();
                if (++read > limit) throw new InvalidDataException();
                return reader.Read();
            }

            string ReadToken(int maximum)
            {
                var value = Read();
                while (value != -1 && char.IsWhiteSpace((char)value)) value = Read();
                var text = new StringBuilder();
                while (value != -1 && !char.IsWhiteSpace((char)value))
                {
                    if (text.Length >= maximum) throw new InvalidDataException();
                    text.Append((char)value);
                    value = Read();
                }

                return text.ToString();
            }

            int ReadHex()
            {
                var value = Read();
                while (value != -1 && char.IsWhiteSpace((char)value)) value = Read();
                return value switch
                {
                    >= '0' and <= '9' => value - '0', >= 'a' and <= 'f' => value - 'a' + 10,
                    >= 'A' and <= 'F' => value - 'A' + 10, _ => throw new InvalidDataException(),
                };
            }
        }
    }
}
