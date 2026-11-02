using System.Text.Json;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    internal sealed partial class CaptureHistoryPersistence
    {
        internal async Task<CaptureHistorySnapshot> LoadAsync(CaptureHistorySnapshot limits,
            CancellationToken token)
        {
            var images = new List<CaptureImageHistoryEntry>();
            var regions = new List<CaptureRegionHistoryEntry>();
            var quarantine = false;
            try
            {
                if (!File.Exists(ManifestPath))
                    return new(true, limits.RegionCapacity, limits.ImageCapacity, limits.MaximumBytes, [], []);
                var manifestBytes = new FileInfo(ManifestPath).Length;
                if (manifestBytes > limits.MaximumBytes)
                {
                    quarantine = true;
                    Logger.ZLogInformation(
                        $"Capture history manifest exceeds the configured byte budget. ManifestBytes={manifestBytes} BudgetBytes={limits.MaximumBytes}");
                    return new(true, limits.RegionCapacity, limits.ImageCapacity, limits.MaximumBytes, [], []);
                }

                CaptureHistoryManifest manifest;
                await using (var stream = new FileStream(ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 65536, true))
                    manifest = await JsonSerializer
                                   .DeserializeAsync(stream, CaptureHistoryJsonContext.Default.CaptureHistoryManifest,
                                       token).ConfigureAwait(false)
                               ?? throw new InvalidDataException("Capture history manifest is empty.");
                if (manifest.Regions is null || manifest.Images is null)
                    throw new InvalidDataException("Capture history manifest collections are missing.");
                if (manifest.Version != 1 || manifest.Regions.Count > 1000 || manifest.Images.Count > 1000)
                    throw new InvalidDataException("Capture history manifest has unsupported data.");
                var ids = new HashSet<Guid>();
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                foreach (var data in manifest.Regions.OrderByDescending(entry => entry?.Timestamp))
                {
                    token.ThrowIfCancellationRequested();
                    if (regions.Count >= limits.RegionCapacity) break;
                    try
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                        if (data is null || data.Id == Guid.Empty || !ids.Add(data.Id))
                            throw new InvalidDataException("Capture region identity is invalid.");
                        ValidateRegion(data.Region);
                        if (data.Shape is { } shape)
                        {
                            ValidateRegion(shape.Bounds);
                            using var path = SKPath.ParseSvgPathData(shape.PathData);
                            if (path is null || path.IsEmpty)
                                throw new InvalidDataException("Capture region shape is invalid.");
                        }

                        if (data.Target is { } target) ValidateWindow(target);
                        if (data.Windows is { } windows)
                            foreach (var window in windows)
                                ValidateWindow(window);
                        if (data is { WindowSource: true, Target: null })
                            throw new InvalidDataException("Capture region target identity is unavailable.");
                        regions.Add(new(new(data.Region, data.WindowSource, data.Target?.ToWindow(), data.Cursor,
                                    data.Shape, data.Windows?.Select(window => window.ToWindow()).ToArray()),
                                data.Timestamp)
                            { Id = data.Id });
                    }
                    catch (Exception error) when (error is InvalidDataException or ArgumentException
                                                      or OverflowException)
                    {
                        quarantine = true;
                        Logger.ZLogWarning(error, $"Capture region history entry could not be restored.");
                    }
                }

                ids.Clear();
                var usedBytes = manifestBytes;
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                foreach (var data in manifest.Images.OrderByDescending(entry => entry?.Timestamp))
                {
                    token.ThrowIfCancellationRequested();
                    if (images.Count >= limits.ImageCapacity) break;
                    try
                    {
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                        if (data is null || data.Id == Guid.Empty || !ids.Add(data.Id) || !Enum.IsDefined(data.Source))
                            throw new InvalidDataException("Capture image history identity is invalid.");
                        ValidateRegion(data.Region);
                        if (!double.IsFinite(data.CaptureScaling) || data.CaptureScaling is < 1 or > 16)
                            throw new InvalidDataException("Capture image history scaling is invalid.");
                        data.Decoration.Validate();
                        if (data.PngBytes < 33 || data.Hdr is { PixelBytes: < 0 })
                            throw new InvalidDataException("Capture image history byte count is invalid.");
                        var size = checked(data.PngBytes + (data.Hdr?.PixelBytes ?? 0));
                        if (size > limits.MaximumBytes - usedBytes) continue;
                        var png = await ReadPayloadAsync(ImagePath(data.Id, false), data.PngBytes, token)
                            .ConfigureAwait(false);
                        var dimensions = PngImageCodec.ReadDimensions(png);
                        CapturedFrame? hdr = null;
                        if (data.Hdr is { } savedHdr)
                        {
                            ValidateHdr(savedHdr, dimensions);
                            var pixels = await ReadPayloadAsync(ImagePath(data.Id, true), savedHdr.PixelBytes, token)
                                .ConfigureAwait(false);
                            hdr = new(savedHdr.Width, savedHdr.Height, savedHdr.Stride, savedHdr.PixelFormat, pixels,
                                savedHdr.ColorInfo, savedHdr.AlphaMode, savedHdr.Revision,
                                Array.AsReadOnly([.. savedHdr.ColorRegions]));
                        }

                        images.Add(new(png, data.Region, data.Source, data.Timestamp, hdr)
                        {
                            Id = data.Id, CaptureScaling = data.CaptureScaling,
                            Decoration = data.Decoration, OutputOrigin = data.OutputOrigin,
                        });
                        usedBytes += size;
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException
                                                      or UnauthorizedAccessException or
                                                      ArgumentException or OverflowException)
                    {
                        quarantine = true;
                        Logger.ZLogWarning(error, $"Capture image history entry could not be restored.");
                    }
                }

                Logger.ZLogInformation(
                    $"Capture history restore completed. RegionCount={regions.Count} ImageCount={images.Count} CacheBytes={usedBytes}");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                              or JsonException or
                                              ArgumentException or OverflowException)
            {
                quarantine = true;
                Logger.ZLogWarning(error, $"Capture history manifest could not be restored.");
            }
            finally
            {
                if (quarantine)
                {
                    DeferInitialWrite = true;
                    _quarantinePending = true;
                    try
                    {
                        QuarantineManifest();
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        Logger.ZLogWarning(error,
                            $"Capture history original manifest could not be quarantined; replacement remains deferred.");
                    }
                }
            }

            return new(true, limits.RegionCapacity, limits.ImageCapacity, limits.MaximumBytes,
                [.. regions], [.. images]);
        }

        private static async Task<byte[]> ReadPayloadAsync(string path, long length, CancellationToken token)
        {
            if (length <= 0 || length > int.MaxValue || new FileInfo(path).Length != length)
                throw new InvalidDataException("Capture history payload length is invalid.");
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            if (bytes.Length != length)
                throw new InvalidDataException("Capture history payload changed during restore.");
            return bytes;
        }

        private static void ValidateRegion(PixelRect region)
        {
            if (region.IsEmpty || (long)region.X + region.Width > int.MaxValue ||
                (long)region.Y + region.Height > int.MaxValue)
                throw new InvalidDataException("Capture history rectangle is invalid.");
        }

        private static void ValidateWindow(CaptureHistoryWindowData? window)
        {
            if (window is null) throw new InvalidDataException("Capture window identity is invalid.");
            ValidateRegion(window.Bounds);
            ValidateRegion(window.Client);
        }

        private static void ValidateHdr(CaptureHistoryHdrData data, (int Width, int Height) dimensions)
        {
            if (data.ColorInfo is null || data.ColorRegions is null)
                throw new InvalidDataException("Capture history HDR color metadata is missing.");
            var bytesPerPixel = data.PixelFormat switch
            {
                CapturePixelFormat.Bgra8Srgb => 4,
                CapturePixelFormat.Rgba16FloatScRgb => 8,
                _ => throw new InvalidDataException("Capture history pixel format is invalid."),
            };
            if (data.Width != dimensions.Width || data.Height != dimensions.Height ||
                data.Stride < (long)data.Width * bytesPerPixel ||
                data.PixelBytes != (long)data.Stride * data.Height || !Enum.IsDefined(data.AlphaMode) ||
                !float.IsFinite(data.ColorInfo.SdrWhiteNits) || data.ColorInfo.SdrWhiteNits <= 0)
                throw new InvalidDataException("Capture history HDR metadata is invalid.");
            var bounds = new PixelRect(0, 0, data.Width, data.Height);
            foreach (var colorRegion in data.ColorRegions)
            {
                // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                if (colorRegion?.ColorInfo is null)
                    throw new InvalidDataException("Capture history color region is invalid.");
                ValidateRegion(colorRegion.Region);
                if (colorRegion.Region.Intersect(bounds) != colorRegion.Region ||
                    !float.IsFinite(colorRegion.ColorInfo.SdrWhiteNits) || colorRegion.ColorInfo.SdrWhiteNits <= 0)
                    throw new InvalidDataException("Capture history color region is invalid.");
            }
        }
    }
}
