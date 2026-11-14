using Avalonia.Input.Platform;
using Avalonia.Threading;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private readonly SemaphoreSlim _inputGate = new(2);

        public async Task<PinnedImageWindow?> PinHdrAsync(byte[] sdrPng, CapturedFrame hdr, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CaptureWindowShape? windowShape = null,
            CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(PrepareNewPinInput, DispatcherPriority.Normal, token);
                PinLogger.ZLogDebug($"HDR pinned image preparation started.");
                var png = await HdrImageService.EncodePngAsync(hdr, token).ConfigureAwait(false);
                var content = await Task.Run(() =>
                        PinnedImageContent.FromHdr(png, PinnedImageContent.Decode(sdrPng, token), hdr), token)
                    .ConfigureAwait(false);
                return await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return Pin(content, position, "", captureScaling, contentOffset, windowShape);
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

        public async Task<PinnedImageWindow?> PinAsync(ReadOnlyMemory<byte> encoded, double captureScaling = 1,
            PixelPoint? position = null, PixelPoint contentOffset = default, CaptureWindowShape? windowShape = null,
            CancellationToken token = default)
        {
            if (!double.IsFinite(captureScaling) || captureScaling is < 1 or > 16)
                throw new ArgumentOutOfRangeException(nameof(captureScaling));
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (encoded.IsEmpty || encoded.Length > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentOutOfRangeException(nameof(encoded));
            await _inputGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await DecodeAndPinAsync(encoded, position, "", captureScaling, contentOffset, windowShape, token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _inputGate.Release();
            }
        }

        private async Task<PinnedImageWindow?> DecodeAndPinAsync(ReadOnlyMemory<byte> encoded, PixelPoint? position,
            string group, double captureScaling = 1, PixelPoint contentOffset = default,
            CaptureWindowShape? windowShape = null, CancellationToken token = default)
        {
            await Dispatcher.UIThread.InvokeAsync(PrepareNewPinInput, DispatcherPriority.Normal, token);
            PinLogger.ZLogDebug($"Pinned image decoding started. EncodedBytes={encoded.Length}");
            try
            {
                var content = await Task.Run(() => PinnedImageContent.Decode(encoded, token), token)
                    .ConfigureAwait(false);
                var window = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return Pin(content, position, group, captureScaling, contentOffset, windowShape);
                }, DispatcherPriority.Normal, token);
                PinLogger.ZLogDebug($"Pinned image decoding completed.");
                return window;
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

        public async Task<bool> PinClipboardAsync(IClipboard clipboard, CancellationToken token = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var result = await PrepareClipboardPinAsync(clipboard, token);
            return (await ConfirmClipboardPinAsync(result, null, token)).Handled;
        }

        private async Task<PinnedInputResult> PrepareClipboardPinAsync(IClipboard clipboard, CancellationToken token)
        {
            using var transfer = await ReadClipboardCandidateAsync(clipboard.TryGetDataAsync, token);
            if (transfer is null) return default;
            var kinds = Preferences.SupportedContentKinds;
            if (kinds == PinnedContentKinds.None) return default;
            var snapshot = await PinnedClipboardSnapshot.ReadEnabledAsync(transfer, kinds, token);
            return await PinInputSnapshotAsync(snapshot, kinds,
                () => PinnedClipboardSnapshot.ReadBitmapAsync(transfer, token), false, token);
        }

        private async Task<PinnedInputResult> PinInputSnapshotAsync(PinnedClipboardSnapshot snapshot,
            PinnedContentKinds kinds, Func<Task<PinnedBitmapInput?>> bitmapFallback, bool isDrop,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            if ((kinds & PinnedContentKinds.Images) != 0)
            {
                foreach (var animationOnly in new[] { true, false })
                    for (var imageIndex = 0; imageIndex < snapshot.Images.Count; imageIndex++)
                    {
                        var (_, bytes) = snapshot.Images[imageIndex];
                        var content = await TryPrepareClipboardAsync(
                            () => Task.Run(() =>
                                animationOnly && AnimationPinMetadata.Read(bytes, token) is null
                                    ? null
                                    : PinnedImageContent.Decode(bytes, token), token), token);
                        if (content is null) continue;
                        return PinSourceImage(snapshot, content, snapshot.ShapeForImage(imageIndex));
                    }

                if (isDrop && await TryPrepareFileImageAsync(snapshot.Files, token) is { } originalImage)
                    return PinSourceImage(snapshot, originalImage, snapshot.ShapeForFile(snapshot.Files[0]));

                var bitmap = snapshot.Bitmap is { } bitmapPng
                    ? new(bitmapPng, snapshot.BitmapWindowShape, snapshot.BitmapCaptureScaling)
                    : isDrop || snapshot.Images.Count > 0
                        ? await ReadClipboardCandidateAsync(bitmapFallback, token)
                        : null;
                if (bitmap is not null)
                {
                    var content = await TryPrepareClipboardAsync(
                        () => Task.Run<PinnedImageContent?>(() => PinnedImageContent.Decode(bitmap.Png, token), token),
                        token);
                    if (content is not null)
                        return PinSourceImage(snapshot, content, (bitmap.WindowShape, bitmap.CaptureScaling));
                }

                if (!isDrop && await TryPrepareFileImageAsync(snapshot.Files, token) is { } fileImage)
                    return PinSourceImage(snapshot, fileImage, snapshot.ShapeForFile(snapshot.Files[0]));
            }

            try
            {
                var usePlainText = (kinds & PinnedContentKinds.Text) != 0 && snapshot.HasPowerPointGenerator;
                if (PinnedContentRegistry.Create(snapshot, kinds, usePlainText) is not { } document) return default;
                token.ThrowIfCancellationRequested();
                return CommitOrDeferText(snapshot, document, usePlainText);
            }
            catch (PinnedHtmlFormatException error)
            {
                var fallback = (kinds & PinnedContentKinds.Text) != 0 && !string.IsNullOrEmpty(snapshot.Text);
                LogClipboardHtmlFallback(error, fallback);
                if (!fallback) throw;
                token.ThrowIfCancellationRequested();
                return CommitOrDeferText(snapshot, new PinnedTextDocument(snapshot.Text!), false);
            }
        }

        private static void LogClipboardHtmlFallback(PinnedHtmlFormatException error, bool fallback)
        {
            PinLogger.ZLogWarning(
                $"Pinned input HTML formatting was rejected. MaximumNestingDepth={PinnedRichParser.MaximumNestingDepth}; PlainTextFallback={fallback}; ErrorType={error.GetType().Name} Code={error.HResult}");
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
    }
}
