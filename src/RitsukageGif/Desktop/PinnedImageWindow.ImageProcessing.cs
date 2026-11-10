using System.Buffers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinnedColorMatrixState _colorMatrixState;
        private PinnedImageContent? _colorContent;
        private byte[]? _colorSourcePng;
        internal Action<IPinnedWindow, double>? OpacityAdjustmentRequested { get; set; }

        private PinnedImageContent? ColorContent => !AnimationActive && !LiveCaptureActive &&
                                                    ReferenceEquals(_colorSourcePng, ProcessingSourcePng)
            ? _colorContent
            : null;

        private byte[] EffectivePng => ColorContent is { } content
            ? content.Hdr is null ? content.Png : content.SdrPng ?? content.Png
            : ProcessingSourceHdr is null
                ? ProcessingSourcePng
                : ProcessingSourceSdrPng;

        private byte[] EffectivePixels => ColorContent?.Pixels ?? ProcessingSourcePixels;
        private CapturedFrame? EffectiveHdr => ColorContent is { } content ? content.Hdr : ProcessingSourceHdr;

        private MenuItem CreateImageProcessingMenu()
        {
            var menu = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageTransform") };
            foreach (var operation in new[]
                     {
                         PinnedImageTransform.RotateClockwise, PinnedImageTransform.RotateCounterclockwise,
                         PinnedImageTransform.FlipHorizontal, PinnedImageTransform.FlipVertical,
                         PinnedImageTransform.Grayscale, PinnedImageTransform.Invert,
                     })
            {
                var item = ProcessingCommand(TransformLabel(operation),
                    () => ApplyImageTransformAsync(operation, _lifetime.Token));
                item.Tag = TransformAction(operation);
                if (operation == PinnedImageTransform.Grayscale) item.ToggleType = MenuItemToggleType.CheckBox;
                menu.Items.Add(item);
            }

            menu.Items.Add(ProcessingSubmenu("PinnedProcessingOpacity", "opacity", delta =>
            {
                AdjustOpacity(delta);
                return Task.CompletedTask;
            }));
            menu.Items.Add(ProcessingSubmenu("PinnedProcessingBrightness", "brightness", delta =>
                ApplyImageProcessingAsync(_colorMatrixState.AdjustBrightness((float)delta), _lifetime.Token)));
            var reset = ProcessingCommand("PinnedProcessingReset", ResetImageProcessingAsync);
            reset.Tag = "reset-processing";
            menu.Items.Add(reset);
            menu.SubmenuOpened += (_, _) =>
            {
                var bindings = BuiltinShortcuts.Snapshot();
                foreach (var item in menu.Items.OfType<MenuItem>())
                {
                    if (item.Tag is not string action) continue;
                    if (ParseTransformAction(action) is { } operation)
                    {
                        item.IsEnabled = CanTransformImage(operation);
                        item.IsChecked = operation == PinnedImageTransform.Grayscale &&
                                         _colorMatrixState.GrayscaleEnabled;
                    }
                    else
                    {
                        item.IsEnabled = !_closed && !_busy && _annotation is null && !IsMouseThrough;
                    }

                    SetProcessingGesture(item, bindings);
                }

                foreach (var child in menu.Items.OfType<MenuItem>().SelectMany(item => item.Items.OfType<MenuItem>()))
                {
                    if (child.Tag is "brightness-increase" or "brightness-decrease")
                        child.IsEnabled = !_closed && !_busy && _annotation is null && !IsMouseThrough;
                    SetProcessingGesture(child, bindings);
                }
            };
            return menu;
        }

        private void SetProcessingGesture(MenuItem item, IReadOnlyList<BuiltinShortcutBinding> bindings)
        {
            item.InputGesture = CurrentPreferences.ShowMenuShortcuts
                ? bindings.FirstOrDefault(binding => binding.ContextKey == BuiltinShortcutContexts.PinnedImage &&
                                                     binding.Action == item.Tag as string)?.Gesture
                : null;
        }

        private void AdjustOpacity(double step)
        {
            if (OpacityAdjustmentRequested is { } adjust) adjust(this, step);
            else Opacity = PinnedOpacityStep.Apply(Opacity, step);
        }

        private MenuItem ProcessingCommand(string label, Func<Task> action)
        {
            var item = new MenuItem { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(label) };
            item.Click += async (_, _) => await RunAsync(action);
            return item;
        }

        private MenuItem ProcessingSubmenu(string label, string actionName, Func<double, Task> action)
        {
            var menu = new MenuItem { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource(label) };
            var increase = ProcessingCommand("PinnedProcessingIncrease10", () => action(.1));
            increase.Tag = actionName + "-increase";
            menu.Items.Add(increase);
            var decrease = ProcessingCommand("PinnedProcessingDecrease10", () => action(-.1));
            decrease.Tag = actionName + "-decrease";
            menu.Items.Add(decrease);
            return menu;
        }

        private async Task ResetImageProcessingAsync()
        {
            await ApplyImageProcessingAsync(default, _lifetime.Token);
            _viewTransform = default;
            CurrentPinZoom = 1;
            Opacity = CurrentPreferences.Opacity;
            _viewport.Offset = default;
            ClearTextSelection();
            UpdateSize();
            RestoreToScreen();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task ApplyImageProcessingAsync(PinnedColorMatrixState state, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_closed || _annotation is not null || IsMouseThrough) return;
            if (LiveCaptureActive || AnimationActive)
            {
                _colorMatrixState = state;
                RefreshMovingColorProcessing();
                RefreshHdrPreview();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            await SerializeRasterUpdateAsync(() => RebuildRasterCoreAsync(state, _activeTranslatedRaster, token));
        }

        private async Task RebuildRasterCoreAsync(PinnedColorMatrixState state,
            PinnedImageTranslationRaster? translated,
            CancellationToken token)
        {
            var originalPng = _png;
            var originalHdr = _hdrSource;
            var source = translated?.Content.Png ?? originalPng;
            var sourceHdr = translated is null ? originalHdr : translated.Content.Hdr;
            var prepared = state.Matrix is null && translated is not null
                ? translated.Content
                : await Task.Run(() => PrepareImageProcessing(source, sourceHdr, state, token), token);
            token.ThrowIfCancellationRequested();
            var bitmap =
                new ScreenshotTiledImage(prepared.Width, prepared.Height, prepared.Pixels, AlphaFormat.Unpremul);
            Bitmap? thumbnail = null;
            var previousBitmap = _bitmap;
            var previousThumbnail = _thumbnail;
            var committed = false;
            try
            {
                using var stream = new MemoryStream(prepared.ThumbnailPng, false);
                thumbnail = new(stream);
                StopAnnotationPreview();
                if (_annotationScene is { } scene)
                    await scene.ReplaceFramesAsync([(new(0, 0, prepared.Width, prepared.Height), prepared.Pixels)],
                        token, Commit);
                else Commit();
                ClearAnnotationPrecisionSource();
                _croppedImage = null;
                UpdateSize();
                RefreshHdrPreview();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                if (committed)
                {
                    previousBitmap.Dispose();
                    previousThumbnail.Dispose();
                }
                else
                {
                    bitmap.Dispose();
                    thumbnail?.Dispose();
                }

                if (!_closed && _annotationScene is { } retained) InitializeAnnotationPreview(retained);
            }

            return;

            void Commit()
            {
                token.ThrowIfCancellationRequested();
                if (_closed || !ReferenceEquals(originalPng, _png) || !ReferenceEquals(originalHdr, _hdrSource))
                    throw new OperationCanceledException(token);
                _bitmap = bitmap;
                _thumbnail = thumbnail;
                if (!ReferenceEquals(_activeTranslatedRaster, translated))
                {
                    _textSelection.Clear();
                    _translatedTextSelection = new(translated?.Text ?? ImageTextSelectionDocument.Empty);
                    _activeTranslatedRaster = translated;
                    if (_annotationScene is { } retained) retained.Underlay = HasTextMarkup ? _textMarkupSession : null;
                    ClearTextSelection();
                }

                _colorMatrixState = state;
                _colorContent = state.Matrix is null ? null : prepared;
                _colorSourcePng = source;
                committed = true;
            }
        }

        private static PinnedImageContent PrepareImageProcessing(byte[] png, CapturedFrame? hdr,
            PinnedColorMatrixState state, CancellationToken token)
        {
            if (hdr is null)
                return PinnedImageContent.Decode(state.Matrix is { } matrix
                    ? TransformPngCore(png, default, null, token, matrix)
                    : png, token);
            var transformed = PinnedColorMatrixProcessor.Apply(hdr, state, token);
            var sdr = HdrToneMapping.Map(transformed, token: token);
            using var image = new MagickImage();
            image.ReadPixels(sdr.Pixels.Span,
                new PixelReadSettings((uint)sdr.Width, (uint)sdr.Height, StorageType.Char, PixelMapping.BGRA));
            image.ColorSpace = ColorSpace.sRGB;
            var preview = PinnedImageContent.Decode(image.ToByteArray(MagickFormat.Png32), token);
            return PinnedImageContent.FromHdr(png, preview, transformed);
        }

        private static (byte[] Png, CapturedFrame? Hdr) ProcessOutputSource(byte[] png, CapturedFrame? hdr,
            PinnedColorMatrixState state, CancellationToken token)
        {
            if (state.Matrix is not { } matrix) return (png, hdr);
            if (hdr is null) return (TransformPngCore(png, default, null, token, matrix), null);
            var transformed = PinnedColorMatrixProcessor.Apply(hdr, state, token);
            var sdr = HdrToneMapping.Map(transformed, token: token);
            using var image = new MagickImage();
            image.ReadPixels(sdr.Pixels.Span,
                new PixelReadSettings((uint)sdr.Width, (uint)sdr.Height, StorageType.Char, PixelMapping.BGRA));
            image.ColorSpace = ColorSpace.sRGB;
            token.ThrowIfCancellationRequested();
            return (image.ToByteArray(MagickFormat.Png32), transformed);
        }

        private void RestoreImageProcessing()
        {
            if ((_colorMatrixState.Matrix is null && _activeTranslatedRaster is null) || AnimationActive ||
                LiveCaptureActive) return;
            var prepared = _colorMatrixState.Matrix is null && _activeTranslatedRaster is not null
                ? _activeTranslatedRaster.Content
                : PrepareImageProcessing(ProcessingSourcePng, ProcessingSourceHdr, _colorMatrixState, _lifetime.Token);
            var bitmap =
                new ScreenshotTiledImage(prepared.Width, prepared.Height, prepared.Pixels, AlphaFormat.Unpremul);
            Bitmap thumbnail;
            try
            {
                using var stream = new MemoryStream(prepared.ThumbnailPng, false);
                thumbnail = new(stream);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            _bitmap.Dispose();
            _thumbnail.Dispose();
            _bitmap = bitmap;
            _thumbnail = thumbnail;
            _colorContent = _colorMatrixState.Matrix is null ? null : prepared;
            _colorSourcePng = ProcessingSourcePng;
            _croppedImage = null;
        }

        private void RefreshMovingColorProcessing()
        {
            if (AnimationActive && _animationFrame is { } animation && _animationSurface is { } surface)
            {
                using var processed = CreateColorPreview(animation.Pixels, animation.Width, animation.Height,
                    animation.HdrPixels is null ? null : FrameHdr(animation, false));
                surface.Update(processed);
            }
            else if (LiveCaptureActive && SnapshotLiveFrame is { } live && _liveSurface is { } liveSurface)
            {
                using var processed = CreateColorPreview(live.Pixels, live.Width, live.Height, live.Hdr);
                liveSurface.Update(processed);
            }

            UpdateVisibleRegion();
        }

        private PreviewFrame CreateColorPreview(byte[] pixels, int width, int height, CapturedFrame? hdr)
        {
            var length = checked(width * height * 4);
            var output = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                if (_colorMatrixState.Matrix is not { } matrix) pixels.AsSpan(0, length).CopyTo(output);
                else if (hdr is not null)
                    HdrToneMapping.Map(PinnedColorMatrixProcessor.Apply(hdr, _colorMatrixState, _lifetime.Token),
                        HdrToneMapMode.HighlightCompression, output, _lifetime.Token);
                else
                    PinnedColorMatrixProcessor.ApplyBgra8(pixels.AsSpan(0, length), output.AsSpan(0, length),
                        matrix, CaptureAlphaMode.Straight);
                return new(output, width, height, 0);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(output);
                throw;
            }
        }
    }
}
