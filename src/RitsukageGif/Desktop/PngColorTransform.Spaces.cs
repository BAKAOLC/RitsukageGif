using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal static partial class PngColorTransform
    {
        public static bool IsSrgbEncoded(IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks)
        {
            return TryGetSimpleGamma(chunks, out var gamma) && gamma is null;
        }

        public static SKColorSpace CreateSourceSpace(IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks,
            CancellationToken token)
        {
            if (chunks.Any(chunk => chunk.Type == "iCCP" || IsRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                                    IsRawProfileChunk(chunk.Type, chunk.Data, "icm")))
            {
                var profile = ReadProfile(chunks, token);
                if (profile.AsSpan(16, 4).SequenceEqual("GRAY"u8))
                    throw new NotSupportedException(
                        "Grayscale ICC conversion to colored RGBA is not supported by the color engine.");
                return SKColorSpace.CreateIcc(profile) ?? throw new InvalidDataException();
            }

            var cicp = chunks.FirstOrDefault(chunk => chunk.Type == "cICP").Data;
            if (!cicp.IsEmpty)
            {
                if (cicp.Length != 4 || cicp.Span[2] != 0 || cicp.Span[3] != 1) throw new InvalidDataException();
                var matrix = cicp.Span[0] switch
                {
                    1 => SKColorSpaceXyz.Srgb, 9 => SKColorSpaceXyz.Rec2020,
                    12 => SKColorSpaceXyz.DisplayP3,
                    _ => throw new NotSupportedException("Unsupported PNG color primaries."),
                };
                var transfer = cicp.Span[1] switch
                {
                    13 => SKColorSpaceTransferFn.Srgb, 8 => SKColorSpaceTransferFn.Linear,
                    1 or 6 => new(1f / .45f, 1f / 1.099f, .099f / 1.099f, 1f / 4.5f, .081f, 0, 0),
                    _ => throw new NotSupportedException("Unsupported SDR PNG transfer function."),
                };
                return SKColorSpace.CreateRgb(transfer, matrix) ?? throw new InvalidDataException();
            }

            if (chunks.Any(chunk => chunk.Type == "sRGB")) return SKColorSpace.CreateSrgb();
            var gamma = chunks.FirstOrDefault(chunk => chunk.Type == "gAMA").Data;
            var chromaticities = chunks.FirstOrDefault(chunk => chunk.Type == "cHRM").Data;
            if (gamma.IsEmpty && chromaticities.IsEmpty) return SKColorSpace.CreateSrgb();
            var function = SKColorSpaceTransferFn.Srgb;
            if (!gamma.IsEmpty)
            {
                if (gamma.Length != 4) throw new InvalidDataException();
                var value = BinaryPrimitives.ReadUInt32BigEndian(gamma.Span) / 100000d;
                if (value <= 0) throw new InvalidDataException();
                function = new((float)(1 / value), 1, 0, 1, 0, 0, 0);
            }

            var xyz = SKColorSpaceXyz.Srgb;
            if (!chromaticities.IsEmpty)
            {
                if (chromaticities.Length != 32) throw new InvalidDataException();

                float Read(int offset)
                {
                    return BinaryPrimitives.ReadUInt32BigEndian(chromaticities.Span[offset..]) / 100000f;
                }

                var primaries = new SKColorSpacePrimaries
                {
                    WX = Read(0), WY = Read(4), RX = Read(8), RY = Read(12),
                    GX = Read(16), GY = Read(20), BX = Read(24), BY = Read(28),
                };
                if (!primaries.ToColorSpaceXyz(out xyz)) throw new InvalidDataException();
            }

            return SKColorSpace.CreateRgb(function, xyz) ?? throw new InvalidDataException();
        }

        public static ushort[] OverlayToSource(byte[] bgra, int width, int height,
            IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks, CancellationToken token)
        {
            if (width <= 0 || height <= 0 || (long)width * height * 4 != bgra.Length)
                throw new ArgumentException("Invalid overlay.", nameof(bgra));
            if (TryGetSimpleGamma(chunks, out var gamma))
            {
                var direct = new ushort[bgra.Length];
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < width; x++)
                    {
                        var index = (y * width + x) * 4;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var encoded = bgra[index + 2 - channel];
                            if (gamma is null)
                            {
                                direct[index + channel] = (ushort)(encoded * 257);
                            }
                            else
                            {
                                var value = encoded / 255d;
                                var linear = value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                                direct[index + channel] = (ushort)Math.Round(Math.Pow(linear, gamma.Value) * 65535);
                            }
                        }

                        direct[index + 3] = (ushort)(bgra[index + 3] * 257);
                    }
                }

                return direct;
            }

            using var source = SKColorSpace.CreateSrgb();
            using var destination = CreateSourceSpace(chunks, token);
            var input = new float[bgra.Length];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    input[i] = bgra[i + 2] / 255f;
                    input[i + 1] = bgra[i + 1] / 255f;
                    input[i + 2] = bgra[i] / 255f;
                    input[i + 3] = bgra[i + 3] / 255f;
                }
            }

            var converted = TransformFloat(input, width, height, source, destination, token);
            var output = new ushort[bgra.Length];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = converted[i + channel];
                        if (!float.IsFinite(value)) throw new InvalidDataException();
                        output[i + channel] = (ushort)Math.Round(Math.Clamp((double)value, 0, 1) * 65535);
                    }

                    output[i + 3] = (ushort)(bgra[i + 3] * 257);
                }
            }

            return output;
        }

        private static bool TryGetSimpleGamma(IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> chunks,
            out double? gamma)
        {
            gamma = null;
            if (chunks.Any(chunk => chunk.Type is "iCCP" or "cHRM" ||
                                    IsRawProfileChunk(chunk.Type, chunk.Data, "icc") ||
                                    IsRawProfileChunk(chunk.Type, chunk.Data, "icm"))) return false;
            var cicp = chunks.FirstOrDefault(chunk => chunk.Type == "cICP").Data;
            if (!cicp.IsEmpty)
            {
                if (cicp.Length != 4 || cicp.Span[0] != 1 || cicp.Span[2] != 0 || cicp.Span[3] != 1) return false;
                if (cicp.Span[1] == 13) return true;
                if (cicp.Span[1] != 8) return false;
                gamma = 1;
                return true;
            }

            if (chunks.Any(chunk => chunk.Type == "sRGB")) return true;
            var chunkGamma = chunks.FirstOrDefault(chunk => chunk.Type == "gAMA").Data;
            if (chunkGamma.IsEmpty) return true;
            if (chunkGamma.Length != 4) throw new InvalidDataException();
            gamma = BinaryPrimitives.ReadUInt32BigEndian(chunkGamma.Span) / 100000d;
            if (gamma <= 0) throw new InvalidDataException();
            return true;
        }

        private static float[] TransformFloat(float[] input, int width, int height, SKColorSpace source,
            SKColorSpace destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var output = new float[input.Length];
            var inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
            try
            {
                var outputHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
                try
                {
                    var sourceInfo = new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Unpremul, source);
                    var targetInfo = new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Unpremul,
                        destination);
                    using var pixmap = new SKPixmap(sourceInfo, inputHandle.AddrOfPinnedObject(), sourceInfo.RowBytes);
                    if (!pixmap.ReadPixels(targetInfo, outputHandle.AddrOfPinnedObject(), targetInfo.RowBytes))
                        throw new InvalidDataException();
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

            token.ThrowIfCancellationRequested();
            return output;
        }
    }
}
