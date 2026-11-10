using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal static class ClipboardImage
    {
        private static readonly SemaphoreSlim CopyGate = new(1);

        public static Task CopyAsync(IClipboard clipboard, ScreenshotResult result,
            ScreenshotClipboardPreferences preferences, IStorageProvider storage, int quality,
            ScreenshotClipboardFileNaming? naming = null, CancellationToken token = default)
        {
            preferences = ScreenshotOutputTransform.ResolveClipboardSize(result, preferences);
            return result.ColorMode == OutputColorMode.Sdr
                ? CopyAsync(clipboard, result.Png, preferences, storage, quality, naming, result.WindowShape,
                    result.CaptureScaling, token)
                : CopyHdrAsync(clipboard, result.Png, result.Hdr ?? throw new InvalidDataException(), result.ColorMode,
                    preferences, storage, quality, naming, result.WindowShape, result.CaptureScaling, token);
        }

        public static Task CopyAsync(IClipboard clipboard, byte[] png, CancellationToken token = default)
        {
            return CopyImageQueuedAsync(clipboard, png, token);
        }

        private static async Task CopyImageQueuedAsync(IClipboard clipboard, byte[] png, CancellationToken token,
            CaptureWindowShape? windowShape = null, double captureScaling = 1)
        {
            await CopyGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                await CopyImageAsync(clipboard, png, windowShape: windowShape, captureScaling: captureScaling);
            }
            finally
            {
                CopyGate.Release();
            }
        }

        private static async Task CopyImageAsync(IClipboard clipboard, byte[] png, byte[]? encodedPng = null,
            CaptureWindowShape? windowShape = null, double captureScaling = 1)
        {
            var transfer = new DataTransfer();
            transfer.Add(CreateImageItem(png, encodedPng, windowShape, captureScaling));
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private static DataTransferItem CreateImageItem(byte[] png, byte[]? encodedPng = null,
            CaptureWindowShape? windowShape = null, double captureScaling = 1)
        {
            var item = new DataTransferItem();
            ClipboardPlatformFormats.SetImage(item, encodedPng ?? png, "image/png", png);
            ClipboardWindowShape.Write(item, windowShape, captureScaling);
            return item;
        }

        private static async Task CopyHdrAsync(IClipboard clipboard, byte[] sdrPng, CapturedFrame hdr,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, ScreenshotClipboardFileNaming? naming = null, CaptureWindowShape? windowShape = null,
            double captureScaling = 1, CancellationToken token = default)
        {
            ValidateHdrCopy(mode, preferences, quality, token);
            ArgumentNullException.ThrowIfNull(sdrPng);
            var transformed = await ScreenshotOutputTransform.ResizeAsync(
                new(sdrPng, ScreenshotOutputAction.Copy, hdr)
                    { ColorMode = mode, WindowShape = windowShape, CaptureScaling = captureScaling },
                preferences.OutputSize, token);
            sdrPng = transformed.Png;
            hdr = transformed.Hdr!;
            var hdrPng = await HdrImageService.EncodePngAsync(hdr, token);
            await CopyHdrPngAsync(clipboard, sdrPng, hdrPng, mode, preferences with { OutputSize = new() }, storage,
                quality, naming, transformed.WindowShape, transformed.CaptureScaling, token);
        }

        private static async Task CopyHdrPngAsync(IClipboard clipboard, byte[] sdrPng, byte[] hdrPng,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, ScreenshotClipboardFileNaming? naming = null, CaptureWindowShape? windowShape = null,
            double captureScaling = 1, CancellationToken token = default)
        {
            ValidateHdrCopy(mode, preferences, quality, token);
            ArgumentNullException.ThrowIfNull(sdrPng);
            ArgumentNullException.ThrowIfNull(hdrPng);
            switch (preferences.FileFormat)
            {
                case ScreenshotClipboardFileFormat.Disabled:
                    await CopyGate.WaitAsync(token);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await CopyImageAsync(clipboard, sdrPng, hdrPng, windowShape, captureScaling);
                    }
                    finally
                    {
                        CopyGate.Release();
                    }

                    return;
                case ScreenshotClipboardFileFormat.Auto:
                    preferences = preferences with { FileFormat = ScreenshotClipboardFileFormat.Png };
                    break;
                case ScreenshotClipboardFileFormat.Png or ScreenshotClipboardFileFormat.Jpeg or
                    ScreenshotClipboardFileFormat.Webp or ScreenshotClipboardFileFormat.Bmp or
                    ScreenshotClipboardFileFormat.Avif:
                    break;
            }

            await CopyHdrFilesAsync(clipboard, sdrPng, hdrPng, mode, preferences, storage, quality,
                new(naming: naming), windowShape, captureScaling, token);
        }

        private static void ValidateHdrCopy(OutputColorMode mode, ScreenshotClipboardPreferences preferences,
            int quality, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            token.ThrowIfCancellationRequested();
            if (mode is not (OutputColorMode.Hdr or OutputColorMode.Both))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (!Enum.IsDefined(preferences.FileFormat))
                throw new ArgumentOutOfRangeException(nameof(preferences));
            if (quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
            if (mode == OutputColorMode.Hdr && preferences.FileFormat is not
                    (ScreenshotClipboardFileFormat.Disabled or ScreenshotClipboardFileFormat.Png
                    or ScreenshotClipboardFileFormat.Auto))
                throw new NotSupportedException(UiText.Get("HdrClipboardRequiresPng"));
        }

        private static async Task CopyHdrFilesAsync(IClipboard clipboard, byte[] sdrPng, byte[] hdrPng,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, ScreenshotClipboardFileStore files, CaptureWindowShape? windowShape,
            double captureScaling, CancellationToken token)
        {
            await CopyGate.WaitAsync(token);
            var paths = new List<string>();
            var publicationAttempted = false;
            try
            {
                await CleanupFilesAsync(clipboard, files);
                if (mode == OutputColorMode.Both)
                    paths.Add(await files.CreateAsync(sdrPng, preferences.FileFormat, quality, token, "-SDR"));
                paths.Add(await files.CreateAsync(hdrPng, ScreenshotClipboardFileFormat.Png, quality, token,
                    mode == OutputColorMode.Both ? "-HDR" : ""));
                var transfer = new DataTransfer();
                foreach (var path in paths)
                {
                    var file = await storage.TryGetFileFromPathAsync(path) ??
                               throw new IOException(UiText.Get("ClipboardFailed"));
                    var item = transfer.Items.Count == 0
                        ? CreateImageItem(sdrPng, hdrPng, windowShape, captureScaling)
                        : new();
                    item.Set(DataFormat.File, file);
                    transfer.Add(item);
                }

                token.ThrowIfCancellationRequested();
                publicationAttempted = true;
                await clipboard.SetDataAsync(transfer);
                await clipboard.FlushAsync();
            }
            catch
            {
                foreach (var path in paths)
                    if (!publicationAttempted || await IsFileReferencedAsync(clipboard, path) == false)
                        files.Delete(path);
                throw;
            }
            finally
            {
                CopyGate.Release();
            }
        }

        private static async Task CopyAsync(IClipboard clipboard, byte[] png,
            ScreenshotClipboardPreferences preferences,
            IStorageProvider storage, int quality, ScreenshotClipboardFileNaming? naming = null,
            CaptureWindowShape? windowShape = null, double captureScaling = 1, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            token.ThrowIfCancellationRequested();
            var transformed = await ScreenshotOutputTransform.ResizeAsync(
                new(png, ScreenshotOutputAction.Copy) { WindowShape = windowShape, CaptureScaling = captureScaling },
                preferences.OutputSize, token);
            png = transformed.Png;
            windowShape = transformed.WindowShape;
            var format = await ResolveFileFormatAsync(png, preferences.FileFormat, token);
            if (format == ScreenshotClipboardFileFormat.Disabled)
                await CopyImageQueuedAsync(clipboard, png, token, windowShape, captureScaling);
            else
                await CopyFileAsync(clipboard, png, format, storage, quality, new(naming: naming), windowShape,
                    captureScaling, token);
        }

        private static ValueTask<ScreenshotClipboardFileFormat> ResolveFileFormatAsync(byte[] png,
            ScreenshotClipboardFileFormat format, CancellationToken token)
        {
            if (format != ScreenshotClipboardFileFormat.Auto) return ValueTask.FromResult(format);
            return new(Task.Run(() =>
            {
                using var image = new MagickImage();
                image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
                try
                {
                    image.Read(png);
                    token.ThrowIfCancellationRequested();
                    var opaque = image.IsOpaque;
                    token.ThrowIfCancellationRequested();
                    return opaque ? ScreenshotClipboardFileFormat.Disabled : ScreenshotClipboardFileFormat.Png;
                }
                catch (MagickException) when (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }
            }, token));
        }

        private static async Task CopyFileAsync(IClipboard clipboard, byte[] png,
            ScreenshotClipboardFileFormat format, IStorageProvider storage, int quality,
            ScreenshotClipboardFileStore files, CaptureWindowShape? windowShape,
            double captureScaling, CancellationToken token)
        {
            await CopyGate.WaitAsync(token);
            try
            {
                await CleanupFilesAsync(clipboard, files);

                var path = await files.CreateAsync(png, format, quality, token);
                var publicationAttempted = false;
                try
                {
                    var file = await storage.TryGetFileFromPathAsync(path) ??
                               throw new IOException(UiText.Get("ClipboardFailed"));
                    token.ThrowIfCancellationRequested();
                    var transfer = new DataTransfer();
                    var item = CreateImageItem(png, windowShape: windowShape, captureScaling: captureScaling);
                    item.Set(DataFormat.File, file);
                    transfer.Add(item);
                    publicationAttempted = true;
                    await clipboard.SetDataAsync(transfer);
                    await clipboard.FlushAsync();
                }
                catch
                {
                    if (!publicationAttempted || await IsFileReferencedAsync(clipboard, path) == false)
                        files.Delete(path);
                    throw;
                }
            }
            finally
            {
                CopyGate.Release();
            }
        }

        private static async Task CleanupFilesAsync(IClipboard clipboard, ScreenshotClipboardFileStore files)
        {
            try
            {
                var currentFiles = await clipboard.TryGetFilesAsync();
                var protectedPaths = currentFiles?.Select(file => file.TryGetLocalPath())
                                         .Where(path => path is not null).Select(path => Path.GetFullPath(path!))
                                         .ToHashSet(OperatingSystem.IsWindows()
                                             ? StringComparer.OrdinalIgnoreCase
                                             : StringComparer.Ordinal)
                                     ?? [];
                await files.CleanupAsync(protectedPaths);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Trace.TraceWarning("Clipboard image cleanup skipped: {0}", error.Message);
            }
        }

        private static async Task<bool?> IsFileReferencedAsync(IClipboard clipboard, string path)
        {
            try
            {
                var files = await clipboard.TryGetFilesAsync();
                return files?.Any(file => string.Equals(file.TryGetLocalPath(), path,
                           OperatingSystem.IsWindows()
                               ? StringComparison.OrdinalIgnoreCase
                               : StringComparison.Ordinal)) ==
                       true;
            }
            catch
            {
                return null;
            }
        }
    }
}
