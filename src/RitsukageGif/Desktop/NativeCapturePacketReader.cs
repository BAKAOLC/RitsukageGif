using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;

namespace RitsukageGif.Desktop
{
    internal sealed class NativeCapturePacketReader(Stream stream)
    {
        private readonly byte[] _header = new byte[32];

        public async ValueTask<NativeCapturePacket> ReadAsync(CancellationToken token)
        {
            await stream.ReadExactlyAsync(_header, token).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(_header) != 0x32434752 ||
                BinaryPrimitives.ReadUInt16LittleEndian(_header.AsSpan(6)) != 32 || _header[5] != 0)
                throw Invalid();
            var kind = (NativeCapturePacketKind)_header[4];
            if (kind is not (NativeCapturePacketKind.Frame or NativeCapturePacketKind.Reply or
                NativeCapturePacketKind.Capabilities or NativeCapturePacketKind.Cursor)) throw Invalid();
            var metadataLength = Length(8);
            var sdrLength = Length(12);
            var hdrLength = Length(16);
            var cursorLength = Length(20);
            var total = (long)sdrLength + hdrLength + cursorLength;
            if (metadataLength is <= 0 or > 65536 || total > 512L * 1024 * 1024) throw Invalid();
            var json = ArrayPool<byte>.Shared.Rent(metadataLength);
            byte[]? payload = null;
            try
            {
                await stream.ReadExactlyAsync(json.AsMemory(0, metadataLength), token).ConfigureAwait(false);
                var metadata = JsonSerializer.Deserialize(json.AsSpan(0, metadataLength),
                    NativeCaptureJsonContext.Default.NativeCaptureMetadata) ?? throw Invalid();
                Validate(kind, metadata, sdrLength, hdrLength, cursorLength);
                if (total != 0)
                {
                    payload = ArrayPool<byte>.Shared.Rent((int)total);
                    await stream.ReadExactlyAsync(payload.AsMemory(0, (int)total), token).ConfigureAwait(false);
                }

                var packet = new NativeCapturePacket(kind, BinaryPrimitives.ReadUInt64LittleEndian(_header.AsSpan(24)),
                    metadata, payload, sdrLength, hdrLength, cursorLength);
                payload = null;
                return packet;
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("Invalid native capture metadata.", error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(json);
                if (payload is not null) ArrayPool<byte>.Shared.Return(payload);
            }
        }

        private int Length(int offset)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(offset));
            return value <= int.MaxValue ? (int)value : throw Invalid();
        }

        private static void Validate(NativeCapturePacketKind kind, NativeCaptureMetadata metadata,
            int sdrLength, int hdrLength, int cursorLength)
        {
            if (kind == NativeCapturePacketKind.Cursor)
            {
                if (sdrLength != 0 || hdrLength != 0) throw Invalid();
                Cursor(metadata.Cursor, cursorLength);
                return;
            }

            if (kind != NativeCapturePacketKind.Frame)
            {
                if (sdrLength != 0 || hdrLength != 0 || cursorLength != 0) throw Invalid();
                return;
            }

            var geometry = metadata.Geometry;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (geometry is null || (geometry.StableIdentity && string.IsNullOrEmpty(geometry.SourceId)) ||
                geometry.Width <= 0 || geometry.Height <= 0 ||
                geometry.PixelWidth <= 0 || geometry.PixelHeight <= 0 || !double.IsFinite(geometry.BackingScale) ||
                geometry.BackingScale <= 0 || geometry.Width > 32768 || geometry.Height > 32768 ||
                geometry.PixelWidth > 32768 || geometry.PixelHeight > 32768 ||
                (long)geometry.X + geometry.Width > int.MaxValue ||
                (long)geometry.Y + geometry.Height > int.MaxValue ||
                (sdrLength == 0 && hdrLength == 0)) throw Invalid();
            var source = metadata.SourceGeometry ?? throw Invalid();
            if (source.Width <= 0 || source.Height <= 0 || source.PixelWidth <= 0 || source.PixelHeight <= 0 ||
                source.Generation != geometry.Generation || source.StreamId != geometry.StreamId ||
                geometry.Bounds.Intersect(source.Bounds) != geometry.Bounds ||
                !double.IsFinite(source.BackingScale) || source.BackingScale <= 0) throw Invalid();
            Plane(metadata.Sdr, sdrLength, false, geometry.PixelWidth, geometry.PixelHeight);
            Plane(metadata.Hdr, hdrLength, true, geometry.PixelWidth, geometry.PixelHeight);
            Cursor(metadata.Cursor, cursorLength);
        }

        private static void Cursor(NativeCaptureCursor? value, int length)
        {
            if ((value is { Valid: true } && (!double.IsFinite(value.PixelScale) || value.PixelScale <= 0 ||
                                              !double.IsFinite(value.X) || !double.IsFinite(value.Y))) ||
                (length != 0 && (value is not { Valid: true, Width: > 0, Height: > 0 } ||
                                 value.Width > 4096 || value.Height > 4096 ||
                                 (long)value.Width * value.Height * 4 != length))) throw Invalid();
        }

        private static void Plane(NativeCapturePlane? plane, int length, bool hdr, int width, int height)
        {
            if (plane is null)
            {
                if (length != 0) throw Invalid();
                return;
            }

            var bytes = plane.Format switch
            {
                "bgra8" when !hdr => 4,
                "rgba16f" or "rgba16" when hdr => 8,
                _ => throw Invalid(),
            };
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (plane.Width != width || plane.Height != height || plane.Stride < (long)width * bytes ||
                (long)plane.Stride * height != length ||
                plane.Alpha is not ("opaque" or "straight" or "premultiplied") ||
                plane.Color is null || !float.IsFinite(plane.Color.SdrWhiteNits) || plane.Color.SdrWhiteNits <= 0 ||
                !float.IsFinite(plane.Color.LinearReferenceNits) || plane.Color.LinearReferenceNits <= 0 ||
                !float.IsFinite(plane.Color.PeakNits) || plane.Color.PeakNits <= 0)
                throw Invalid();
        }

        private static InvalidDataException Invalid()
        {
            return new("Invalid native capture packet.");
        }
    }
}
