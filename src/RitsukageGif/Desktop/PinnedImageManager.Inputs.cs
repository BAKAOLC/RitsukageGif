using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly SemaphoreSlim _inputGate = new(2);

        public async Task PinHdrAsync(byte[] sdrPng, CapturedFrame hdr, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                PinLogger.ZLogDebug($"HDR pinned image preparation started.");
                var png = await HdrImageService.EncodePngAsync(hdr, token).ConfigureAwait(false);
                var content = await Task.Run(() =>
                        PinnedImageContent.FromHdr(png, PinnedImageContent.Decode(sdrPng, token), hdr), token)
                    .ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Pin(content, position, "", captureScaling, contentOffset);
                }, DispatcherPriority.Normal, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"HDR pinned image preparation cancelled.");
                throw;
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"HDR pinned image preparation failed.");
                throw;
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public async Task PinAsync(ReadOnlyMemory<byte> encoded, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (encoded.IsEmpty || encoded.Length > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await DecodeAndPinAsync(encoded, position, "", captureScaling, contentOffset, token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        private async Task DecodeAndPinAsync(ReadOnlyMemory<byte> encoded, PixelPoint? position,
            string group, double captureScaling = 1, PixelPoint contentOffset = default,
            CancellationToken token = default)
        {
            PinLogger.ZLogDebug($"Pinned image decoding started. EncodedBytes={encoded.Length}");
            try
            {
                var content = await Task.Run(() => PinnedImageContent.Decode(encoded, token), token)
                    .ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    Pin(content, position, group, captureScaling, contentOffset);
                }, DispatcherPriority.Normal, token);
                PinLogger.ZLogDebug($"Pinned image decoding completed.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PinLogger.ZLogDebug($"Pinned image decoding cancelled.");
                throw;
            }
            catch (Exception error)
            {
                PinLogger.ZLogError(error, $"Pinned image decoding or presentation failed.");
                throw;
            }
        }

        private async Task PinFileAsync(string path, PixelPoint? position = null, string group = "",
            CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            PinLogger.ZLogDebug($"Pinned image file open requested.");
            var file = new FileInfo(Path.GetFullPath(path));
            if (file.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(path));
            await _inputGate.WaitAsync(token);
            try
            {
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length is <= 0 or > PinnedImageContent.MaximumEncodedBytes)
                    throw new ArgumentOutOfRangeException(nameof(path));
                var encoded = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(encoded, token);
                await DecodeAndPinAsync(encoded, position, group, token: token);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        public async Task<bool> PinClipboardAsync(IClipboard clipboard, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            using (var transfer = await clipboard.TryGetDataAsync())
                if (transfer is not null)
                    foreach (var name in OperatingSystem.IsWindows() ? new[] { "image/png", "PNG" } : ["image/png"])
                    {
                        var format = DataFormat.CreateBytesPlatformFormat(name);
                        foreach (var item in transfer.Items)
                        {
                            token.ThrowIfCancellationRequested();
                            if (await item.TryGetRawAsync(format) is not byte[] { Length: > 0 } png) continue;
                            await PinAsync(png, token: token);
                            return true;
                        }
                    }

            using var bitmap = await clipboard.TryGetBitmapAsync();
            token.ThrowIfCancellationRequested();
            if (bitmap is not null)
            {
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 128L * 1024 * 1024)
                    throw new InvalidOperationException(UiText.Get("InvalidFrameBuffer"));
                await _inputGate.WaitAsync(token);
                try
                {
                    using var stream = new MemoryStream();
                    await Task.Run(() => bitmap.Save(stream), token);
                    var data = stream.ToArray();
                    await DecodeAndPinAsync(data, null, "", token: token);
                    return true;
                }
                finally
                {
                    _inputGate.Release();
                }
            }

            var files = await clipboard.TryGetFilesAsync();
            token.ThrowIfCancellationRequested();
            var file = files?.OfType<IStorageFile>().FirstOrDefault();
            if (file?.TryGetLocalPath() is not { } path) return false;
            await PinFileAsync(path, token: token);
            return true;
        }

        // ReSharper disable once UnusedMethodReturnValue.Global
        public async Task<bool> OpenImageAsync(Window owner, CancellationToken token = default)
        {
            PinLogger.ZLogDebug($"Pinned image file picker requested.");
            var files = await owner.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = UiText.Get("PinnedImageOpen"), AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll],
            });
            token.ThrowIfCancellationRequested();
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            {
                PinLogger.ZLogDebug($"Pinned image file picker cancelled or returned no local image.");
                return false;
            }

            await PinFileAsync(path, token: token);
            return true;
        }
    }
}
