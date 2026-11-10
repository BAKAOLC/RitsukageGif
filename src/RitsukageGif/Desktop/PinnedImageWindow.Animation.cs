using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private CancellationTokenSource? _animationCancellation;
        private AnimationPinDecoder? _animationDecoder;
        private int _animationDisplayed = -1;
        private byte[]? _animationEncoded;
        private PreviewFrame? _animationFrame;
        private AnimationPinPlayback? _animationPlayback;
        private int _animationRequested = -1;
        private AnimationPinSurface? _animationSurface;
        private DispatcherTimer? _animationTimer;
        private bool _animationVisiblePlaying;
        private Task? _animationWork;

        internal bool AnimationActive { get; private set; }

        private AnimationPinMetadata? AnimationMetadata { get; set; }

        private void InitializeAnimation(PinnedImageContent content)
        {
            _animationEncoded = content.OriginalEncoded;
            AnimationMetadata = content.Animation;
            Opened += (_, _) => StartAnimation();
            PropertyChanged += (_, args) =>
            {
                if (args.Property != IsVisibleProperty && args.Property != WindowStateProperty) return;
                var visible = IsVisible && WindowState != WindowState.Minimized;
                switch (visible)
                {
                    case false when _animationPlayback is { Playing: true }:
                        _animationVisiblePlaying = true;
                        _animationPlayback.Pause();
                        _animationTimer?.Stop();
                        break;
                    case true when _animationVisiblePlaying:
                        _animationVisiblePlaying = false;
                        StartAnimation();
                        break;
                }
            };
            if (_animationEncoded is not null && AnimationMetadata is not null)
                ConfigureAnimation(content.AnimationActive);
        }

        private void ConfigureAnimation(bool active)
        {
            var metadata = AnimationMetadata!;
            AnimationActive = active;
            if (!active) return;
            if (metadata.Width != (int)_bitmap.Size.Width || metadata.Height != (int)_bitmap.Size.Height)
                throw new InvalidDataException();
            _animationDecoder = new(_animationEncoded!, metadata);
            _animationPlayback = new(metadata);
            _animationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
            _animationTimer.Tick += (_, _) => TickAnimation();
            PinLogger.ZLogInformation(
                $"Animated pinned image initialized. Format={metadata.Extension} Frames={metadata.Frames.Count} Width={metadata.Width} Height={metadata.Height}");
        }

        private void StartAnimation()
        {
            if (!AnimationActive || _closed || _animationPlayback is null) return;
            _animationPlayback.Play();
            TickAnimation();
            _animationTimer?.Start();
        }

        private void TickAnimation()
        {
            if (!AnimationActive || _closed || _animationPlayback is null) return;
            _animationRequested = _animationPlayback.FrameIndex;
            if (_animationDisplayed != _animationRequested && _animationWork is null)
                _animationWork = DecodeAnimationAsync();
            if (_animationPlayback.Playing) _animationTimer!.Interval = _animationPlayback.NextDelay;
            else _animationTimer?.Stop();
        }

        private async Task DecodeAnimationAsync()
        {
            var decoder = _animationDecoder!;
            var cancellation = _animationCancellation!;
            await Task.Yield();
            try
            {
                do
                {
                    var index = _animationRequested;
                    using var frame = await Task.Run(() => decoder.Read(index, cancellation.Token), cancellation.Token);
                    if (_closed || !AnimationActive || !ReferenceEquals(decoder, _animationDecoder)) return;
                    if (index != _animationRequested) continue;
                    _animationSurface ??= new(frame.Width, frame.Height);
                    if (_colorMatrixState.Matrix is null)
                    {
                        _animationSurface.Update(frame);
                    }
                    else
                    {
                        using var processed = CreateColorPreview(frame.Pixels, frame.Width, frame.Height,
                            frame.HdrPixels is null ? null : FrameHdr(frame, false));
                        _animationSurface.Update(processed);
                    }

                    _animationFrame?.Dispose();
                    _animationFrame = frame.Retain();
                    _animationDisplayed = index;
                    UpdateVisibleRegion();
                    if (frame.HdrPixels is not null) RefreshHdrPreview();
                } while (_animationDisplayed != _animationRequested);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                _animationPlayback?.Pause();
                _animationTimer?.Stop();
                PinLogger.ZLogError(error, $"Animated pinned image decoding failed.");
                if (!_closed)
                {
                    _status.Text = error.Message;
                    _status.IsVisible = true;
                }
            }
            finally
            {
                _animationWork = null;
                if (_closed || !AnimationActive || !ReferenceEquals(decoder, _animationDecoder))
                {
                    decoder.Dispose();
                    cancellation.Dispose();
                }
            }
        }

        private bool TryRefreshAnimationHdr()
        {
            if (!AnimationActive || _animationFrame is not { HdrPixels: not null } frame ||
                _hdrPreview is null || _annotation is not null || HasTextSelection) return false;
            var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _hdrPreviewWork = work;
            _ = RefreshAnimationHdrAsync(frame.Retain(), work);
            return true;
        }

        private async Task RefreshAnimationHdrAsync(PreviewFrame frame, CancellationTokenSource work)
        {
            using (frame)
                await RefreshHdrPreviewAsync(FrameHdr(frame, false), work);
        }

        private void DisposeAnimation()
        {
            AnimationActive = false;
            _animationVisiblePlaying = false;
            _animationTimer?.Stop();
            _animationTimer = null;
            _animationCancellation?.Cancel();
            _animationFrame?.Dispose();
            _animationFrame = null;
            if (_animationSurface is { } surface)
            {
                if (ReferenceEquals(_image.Source, surface)) _image.Source = _bitmap;
                surface.Dispose();
            }

            _animationSurface = null;
            _animationDisplayed = -1;
            if (_animationWork is null)
            {
                _animationDecoder?.Dispose();
                _animationCancellation?.Dispose();
            }

            _animationDecoder = null;
            _animationCancellation = null;
            _animationPlayback = null;
        }

        private Task<ScreenshotResult>? CaptureAnimationFrameAsync(OutputColorMode mode, CancellationToken token)
        {
            if (!AnimationActive || _animationFrame is null) return null;
            var frame = _animationFrame.Retain();
            var scale = OriginalCaptureScaling;
            var colorState = _colorMatrixState;
            // ReSharper disable once MethodSupportsCancellation
            return Task.Run(() =>
            {
                using (frame)
                {
                    token.ThrowIfCancellationRequested();
                    var png = EncodeAnimationFrame(frame, token);
                    var hdr = frame.HdrPixels is null ? null : FrameHdr(frame);
                    (png, hdr) = ProcessOutputSource(png, hdr, colorState, token);
                    return CreateOutput(png, hdr, mode, scale);
                }
            });
        }

        private async Task FreezeAnimationFrameAsync(CancellationToken token)
        {
            if (!AnimationActive) return;
            _animationPlayback?.Pause();
            _animationTimer?.Stop();
            if (_animationWork is { } pending) await pending;
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_animationFrame is not { } displayed)
            {
                DisposeAnimation();
                return;
            }

            using var frame = displayed.Retain();
            var sdrPng = await Task.Run(() => EncodeAnimationFrame(frame, token), token);
            var hdr = frame.HdrPixels is null ? null : FrameHdr(frame);
            var png = hdr is null ? sdrPng : await HdrImageService.EncodePngAsync(hdr, token);
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            var pixels = frame.Pixels.AsSpan(0, checked(frame.Width * frame.Height * 4)).ToArray();
            var bitmap = new ScreenshotTiledImage(frame.Width, frame.Height, pixels, AlphaFormat.Unpremul);
            Bitmap thumbnail;
            try
            {
                using var stream = new MemoryStream(sdrPng, false);
                thumbnail = new(stream);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            DisposeAnimation();
            ResetTextRecognition();
            _bitmap.Dispose();
            _thumbnail.Dispose();
            _bitmap = bitmap;
            _thumbnail = thumbnail;
            _png = png;
            _pixels = pixels;
            ContentHash = Convert.ToHexString(SHA256.HashData(png));
            Id = Guid.NewGuid().ToString("N");
            _sdrPng = hdr is null ? null : sdrPng;
            _image.Source = bitmap;
            ConfigureHdr(hdr);
            RestoreImageProcessing();
            UpdateSize();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static CapturedFrame FrameHdr(PreviewFrame frame, bool copy = true)
        {
            ReadOnlyMemory<byte> pixels = frame.HdrPixels!.AsMemory(0, checked(frame.Width * frame.Height * 8));
            if (copy) pixels = pixels.ToArray();
            return new(frame.Width, frame.Height, frame.Width * 8, CapturePixelFormat.Rgba16FloatScRgb,
                pixels, new(true, frame.HdrReferenceWhiteLevel * FrameColorInfo.ScRgbReferenceWhiteNits),
                frame.HdrAlphaMode, 0, []);
        }

        private static byte[] EncodeAnimationFrame(PreviewFrame frame, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var bitmap = new SKBitmap(new(frame.Width, frame.Height, SKColorType.Bgra8888,
                SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(frame.Pixels, y * frame.Width * 4, bitmap.GetPixels() + y * bitmap.RowBytes,
                    frame.Width * 4);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidDataException();
            return encoded.ToArray();
        }

        private void AddAnimationCommands(ContextMenu menu)
        {
            var root = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedAnimation") };
            var play = AnimationCommand("PinnedAnimationPlay", ToolbarIcon.GetDefinition("PinnedAnimationPlay"), () =>
            {
                StartAnimation();
                return Task.CompletedTask;
            });
            var pause = AnimationCommand("PinnedAnimationPause", ToolbarIcon.GetDefinition("PinnedAnimationPause"),
                () =>
                {
                    _animationPlayback?.Pause();
                    _animationVisiblePlaying = false;
                    _animationTimer?.Stop();
                    return Task.CompletedTask;
                });
            root.Items.Add(play);
            root.Items.Add(pause);
            root.Items.Add(AnimationCommand("PinnedAnimationPrevious",
                ToolbarIcon.GetDefinition("PinnedAnimationPrevious"),
                () => StepAnimation(-1)));
            root.Items.Add(AnimationCommand("PinnedAnimationNext", ToolbarIcon.GetDefinition("PinnedAnimationNext"),
                () => StepAnimation(1)));
            var speed = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedAnimationSpeed") };
            foreach (var value in new[] { 0.25, 0.5, 1, 2, 4 })
            {
                var item = new MenuItem
                {
                    Header = value.ToString("0.##", CultureInfo.InvariantCulture) + "×",
                    ToggleType = MenuItemToggleType.Radio,
                };
                item.Click += (_, _) =>
                {
                    if (!CanControlAnimation) return;
                    _animationPlayback?.SetSpeed(value);
                    TickAnimation();
                };
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                menu.Opened += (_, _) => item.IsChecked = _animationPlayback?.Speed == value;
                speed.Items.Add(item);
            }

            root.Items.Add(speed);
            root.Items.Add(new Separator());
            root.Items.Add(AnimationCommand("PinnedAnimationCopyOriginal", ToolbarIcon.GetDefinition("ScreenshotCopy"),
                CopyOriginalAnimationAsync));
            root.Items.Add(AnimationCommand("PinnedAnimationSaveOriginal", ToolbarIcon.GetDefinition("ScreenshotSave"),
                SaveOriginalAnimationAsync));
            menu.Opened += (_, _) =>
            {
                root.IsVisible = _animationEncoded is not null;
                play.IsEnabled = CanControlAnimation && _animationPlayback is { Playing: false };
                pause.IsEnabled = CanControlAnimation && _animationPlayback is { Playing: true };
                speed.IsEnabled = CanControlAnimation;
                foreach (var item in root.Items.OfType<MenuItem>().Skip(2).Take(2))
                    item.IsEnabled = CanControlAnimation;
            };
            menu.Items.Add(root);
        }

        private MenuItem AnimationCommand(string key, IconDefinition definition, Func<Task> action)
        {
            var item = Command(key, action);
            item.Icon = ToolbarIcon.Create(item, definition);
            return item;
        }

        private Task StepAnimation(int direction)
        {
            if (!AnimationActive || _animationPlayback is null) return Task.CompletedTask;
            _animationPlayback.Step(direction);
            _animationVisiblePlaying = false;
            _animationTimer?.Stop();
            TickAnimation();
            return _animationWork ?? Task.CompletedTask;
        }

        private async Task CopyOriginalAnimationAsync()
        {
            var encoded = _animationEncoded ?? throw new InvalidOperationException();
            var metadata = AnimationMetadata ?? throw new InvalidOperationException();
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            var preview = OriginalContent.SdrPng ?? OriginalContent.Png;
            ClipboardPlatformFormats.SetImage(item, encoded, metadata.MimeType, preview);
            ClipboardWindowShape.Write(item, Presentation.WindowShape, OriginalCaptureScaling);
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer);
            await clipboard.FlushAsync();
        }

        private async Task SaveOriginalAnimationAsync()
        {
            var token = _lifetime.Token;
            token.ThrowIfCancellationRequested();
            var encoded = _animationEncoded ?? throw new InvalidOperationException();
            var metadata = AnimationMetadata ?? throw new InvalidOperationException();
            using var file = await StoragePickerAwaiter.WaitAsync(StorageProvider.SaveFilePickerAsync(new()
            {
                Title = UiText.Get("PinnedAnimationSaveOriginal"),
                SuggestedFileName = "Animation" + metadata.Extension,
                DefaultExtension = metadata.Extension.TrimStart('.'),
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new(metadata.Extension.TrimStart('.').ToUpperInvariant()) { Patterns = ["*" + metadata.Extension] },
                ],
            }), token);
            if (file is null) return;
            token.ThrowIfCancellationRequested();
            var path = file.TryGetLocalPath() ?? throw new NotSupportedException(UiText.Get("FileOperationFailed"));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, encoded, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            SavedPathChanged?.Invoke(path);
            PinLogger.ZLogInformation(
                $"Original animated pinned image saved. Format={metadata.Extension} Bytes={encoded.Length}");
        }
    }
}
