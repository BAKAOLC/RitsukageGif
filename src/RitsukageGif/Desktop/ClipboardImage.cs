using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal static class ClipboardImage
    {
        private static readonly SemaphoreSlim CopyGate = new(1);

        public static Task CopyAsync(IClipboard clipboard, ScreenshotResult result,
            ScreenshotClipboardPreferences preferences, IStorageProvider storage, int quality,
            CancellationToken token = default)
        {
            preferences = ScreenshotOutputTransform.ResolveClipboardSize(result, preferences);
            return result.ColorMode == OutputColorMode.Sdr
                ? CopyAsync(clipboard, result.Png, preferences, storage, quality, token)
                : CopyHdrAsync(clipboard, result.Png, result.Hdr ?? throw new InvalidDataException(), result.ColorMode,
                    preferences, storage, quality, token);
        }

        public static Task CopyAsync(IClipboard clipboard, byte[] png)
        {
            return CopyImageQueuedAsync(clipboard, png, CancellationToken.None);
        }

        private static async Task CopyImageQueuedAsync(IClipboard clipboard, byte[] png, CancellationToken token)
        {
            await CopyGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                await CopyImageAsync(clipboard, png);
            }
            finally
            {
                CopyGate.Release();
            }
        }

        private static async Task CopyImageAsync(IClipboard clipboard, byte[] png, byte[]? encodedPng = null)
        {
            var transfer = new DataTransfer();
            var item = DataTransferItem.Create(DataFormat.Bitmap, () =>
            {
                using var stream = new MemoryStream(png, false);
                return new(stream);
            });
            item.Set(DataFormat.CreateBytesPlatformFormat("image/png"), encodedPng ?? png);
            if (OperatingSystem.IsWindows()) item.Set(DataFormat.CreateBytesPlatformFormat("PNG"), encodedPng ?? png);
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private static async Task CopyHdrAsync(IClipboard clipboard, byte[] sdrPng, CapturedFrame hdr,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, CancellationToken token = default)
        {
            ValidateHdrCopy(mode, preferences, quality, token);
            ArgumentNullException.ThrowIfNull(sdrPng);
            var transformed = await ScreenshotOutputTransform.ResizeAsync(
                new(sdrPng, ScreenshotOutputAction.Copy, hdr) { ColorMode = mode }, preferences.OutputSize, token);
            sdrPng = transformed.Png;
            hdr = transformed.Hdr!;
            var hdrPng = await HdrImageService.EncodePngAsync(hdr, token);
            await CopyHdrPngAsync(clipboard, sdrPng, hdrPng, mode, preferences with { OutputSize = new() }, storage,
                quality, token);
        }

        private static async Task CopyHdrPngAsync(IClipboard clipboard, byte[] sdrPng, byte[] hdrPng,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, CancellationToken token = default)
        {
            ValidateHdrCopy(mode, preferences, quality, token);
            ArgumentNullException.ThrowIfNull(sdrPng);
            ArgumentNullException.ThrowIfNull(hdrPng);
            if (preferences.Mode == ScreenshotClipboardMode.Image)
            {
                await CopyGate.WaitAsync(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    await CopyImageAsync(clipboard, sdrPng, hdrPng);
                }
                finally
                {
                    CopyGate.Release();
                }

                return;
            }

            if (preferences.Mode != ScreenshotClipboardMode.File)
                throw new ArgumentOutOfRangeException(nameof(preferences));
            await CopyHdrFilesAsync(clipboard, sdrPng, hdrPng, mode, preferences, storage, quality, new(), token);
        }

        private static void ValidateHdrCopy(OutputColorMode mode, ScreenshotClipboardPreferences preferences,
            int quality, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            token.ThrowIfCancellationRequested();
            if (mode is not (OutputColorMode.Hdr or OutputColorMode.Both))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (!Enum.IsDefined(preferences.Mode) || !Enum.IsDefined(preferences.FileFormat))
                throw new ArgumentOutOfRangeException(nameof(preferences));
            if (quality is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(quality));
            if (mode == OutputColorMode.Hdr && preferences.Mode == ScreenshotClipboardMode.File &&
                preferences.FileFormat != ScreenshotClipboardFileFormat.Png)
                throw new NotSupportedException(UiText.Get("HdrClipboardRequiresPng"));
        }

        private static async Task CopyHdrFilesAsync(IClipboard clipboard, byte[] sdrPng, byte[] hdrPng,
            OutputColorMode mode, ScreenshotClipboardPreferences preferences, IStorageProvider storage,
            int quality, ScreenshotClipboardFileStore files, CancellationToken token)
        {
            await CopyGate.WaitAsync(token);
            var paths = new List<string>();
            var publicationAttempted = false;
            try
            {
                await CleanupFilesAsync(clipboard, files);
                if (mode == OutputColorMode.Both)
                    paths.Add(await files.CreateAsync(sdrPng, preferences.FileFormat, quality, token));
                paths.Add(await files.CreateAsync(hdrPng, ScreenshotClipboardFileFormat.Png, quality, token));
                var transfer = new DataTransfer();
                foreach (var path in paths)
                {
                    var file = await storage.TryGetFileFromPathAsync(path) ??
                               throw new IOException(UiText.Get("ClipboardFailed"));
                    transfer.Add(DataTransferItem.CreateFile(file));
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
            IStorageProvider storage, int quality, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            token.ThrowIfCancellationRequested();
            var transformed = await ScreenshotOutputTransform.ResizeAsync(new(png, ScreenshotOutputAction.Copy),
                preferences.OutputSize, token);
            png = transformed.Png;
            await (preferences.Mode switch
            {
                ScreenshotClipboardMode.Image => CopyImageQueuedAsync(clipboard, png, token),
                ScreenshotClipboardMode.File => CopyFileAsync(clipboard, png, preferences.FileFormat, storage,
                    quality, new(), token),
                _ => throw new ArgumentOutOfRangeException(nameof(preferences)),
            });
        }

        private static async Task CopyFileAsync(IClipboard clipboard, byte[] png,
            ScreenshotClipboardFileFormat format, IStorageProvider storage, int quality,
            ScreenshotClipboardFileStore files, CancellationToken token)
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
                    transfer.Add(DataTransferItem.CreateFile(file));
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
