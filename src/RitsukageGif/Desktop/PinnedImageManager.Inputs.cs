using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageMagick;

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
            using var transfer = await ReadClipboardCandidateAsync(clipboard.TryGetDataAsync, token);
            if (transfer is null) return false;
            var kinds = Preferences.SupportedContentKinds;
            if (kinds == PinnedContentKinds.None) return false;
            var snapshot = await PinnedClipboardSnapshot.ReadEnabledAsync(transfer, kinds, token);
            var result = await PinInputSnapshotAsync(snapshot, kinds,
                () => PinnedClipboardSnapshot.ReadBitmapAsync(transfer, token), false, token);
            return result.Handled;
        }

        private async Task<PinnedInputResult> PinInputSnapshotAsync(PinnedClipboardSnapshot snapshot,
            PinnedContentKinds kinds, Func<Task<byte[]?>> bitmapFallback, bool preferFileImage, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            if ((kinds & PinnedContentKinds.Images) != 0)
            {
                foreach (var animationOnly in new[] { true, false })
                foreach (var (_, bytes) in snapshot.Images)
                {
                    var content = await TryPrepareClipboardAsync(
                        () => Task.Run(() =>
                            animationOnly && AnimationPinMetadata.Read(bytes, token) is null
                                ? null
                                : PinnedImageContent.Decode(bytes, token), token), token);
                    if (content is null) continue;
                    return new(true, Pin(content, null, ""));
                }

                if (preferFileImage && await TryPrepareFileImageAsync(snapshot.Files, token) is { } originalImage)
                    return new(true, Pin(originalImage, null, ""));

                var bitmap = snapshot.Bitmap ?? (preferFileImage || snapshot.Images.Count > 0
                    ? await ReadClipboardCandidateAsync(bitmapFallback, token)
                    : null);
                if (bitmap is not null)
                {
                    var content = await TryPrepareClipboardAsync(
                        () => Task.Run<PinnedImageContent?>(() => PinnedImageContent.Decode(bitmap, token), token),
                        token);
                    if (content is not null) return new(true, Pin(content, null, ""));
                }

                if (!preferFileImage && await TryPrepareFileImageAsync(snapshot.Files, token) is { } fileImage)
                    return new(true, Pin(fileImage, null, ""));
            }

            try
            {
                if (PinnedContentRegistry.Create(snapshot, kinds) is not { } document) return default;
                token.ThrowIfCancellationRequested();
                return new(true, PinDocument(document));
            }
            catch (PinnedHtmlFormatException error)
            {
                var fallback = (kinds & PinnedContentKinds.Text) != 0 && !string.IsNullOrEmpty(snapshot.Text);
                PinLogger.ZLogWarning(
                    $"Pinned input HTML formatting was rejected. MaximumNestingDepth={PinnedRichParser.MaximumNestingDepth}; PlainTextFallback={fallback}; ErrorType={error.GetType().Name} Code={error.HResult}");
                if (!fallback) throw;
                token.ThrowIfCancellationRequested();
                return new(true, PinDocument(new PinnedTextDocument(snapshot.Text!)));
            }
        }

        private async Task<PinnedImageContent?> TryPrepareClipboardAsync(Func<Task<PinnedImageContent?>> prepare,
            CancellationToken token)
        {
            await _inputGate.WaitAsync(token);
            try
            {
                var content = await prepare();
                token.ThrowIfCancellationRequested();
                return content;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException or OverflowException or
                                              MagickException)
            {
                token.ThrowIfCancellationRequested();
                PinLogger.ZLogWarning(
                    $"Pinned image input candidate could not be decoded; trying another format. ErrorType={error.GetType().Name} Code={error.HResult}");
                return null;
            }
            finally
            {
                _inputGate.Release();
            }
        }

        private static async Task<T?> ReadClipboardCandidateAsync<T>(Func<Task<T?>> read, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var value = await read();
                token.ThrowIfCancellationRequested();
                return value;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException or
                                              MagickException)
            {
                token.ThrowIfCancellationRequested();
                PinLogger.ZLogWarning(
                    $"Pinned image clipboard format could not be read; trying another format. ErrorType={error.GetType().Name} Code={error.HResult}");
                return default;
            }
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
