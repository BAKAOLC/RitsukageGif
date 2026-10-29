using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using Path = Avalonia.Controls.Shapes.Path;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool _transformCommandsInitialized;

        private void EnsureTransformCommands()
        {
            if (_transformCommandsInitialized || _content.ContextMenu is not { } menu) return;
            _transformCommandsInitialized = true;
            var transforms = new MenuItem
                { [!HeaderedSelectingItemsControl.HeaderProperty] = LiveText.Resource("PinnedImageTransform") };
            foreach (var transform in Enum.GetValues<PinnedImageTransform>())
            {
                var action = TransformAction(transform);
                var item = Command(TransformLabel(transform),
                    () => ApplyImageTransformAsync(transform, _lifetime.Token));
                item.Tag = action;
                item.Icon = new Path
                {
                    Data = Geometry.Parse(transform switch
                    {
                        PinnedImageTransform.RotateClockwise or PinnedImageTransform.Rotate180 =>
                            "M5,8 A8,8 0 1 1 4,16 M5,3 V8 H10",
                        PinnedImageTransform.FlipHorizontal => "M12,2 V22 M9,5 L2,12 9,19 Z M15,5 L22,12 15,19 Z",
                        PinnedImageTransform.FlipVertical => "M2,12 H22 M5,9 L12,2 19,9 Z M5,15 L12,22 19,15 Z",
                        _ => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 M12,2 V22",
                    }),
                    Width = 16, Height = 16, Stretch = Stretch.Uniform, StrokeThickness = 1.6,
                    [!Shape.StrokeProperty] = item[!ForegroundProperty],
                };
                transforms.Items.Add(item);
            }

            menu.Items.Insert(Math.Min(5, menu.Items.Count), transforms);
            menu.Opened += (_, _) =>
            {
                var bindings = BuiltinShortcuts.Snapshot();
                foreach (var item in transforms.Items.OfType<MenuItem>())
                {
                    var transform = ParseTransformAction((string)item.Tag!)!.Value;
                    item.IsEnabled = CanTransformImage(transform);
                    item.InputGesture = CurrentPreferences.ShowMenuShortcuts
                        ? bindings.FirstOrDefault(binding =>
                                binding.ContextKey == "BuiltinShortcutPinnedContext" &&
                                binding.Action == (string)item.Tag!)
                            ?.Gesture
                        : null;
                }
            };
            AddHandler(KeyDownEvent, OnTransformKeyDown, RoutingStrategies.Tunnel);
        }

        private void OnTransformKeyDown(object? sender, KeyEventArgs args)
        {
            if (_annotation is not null || IsAnnotationTextInputKey(args.Key) ||
                IsOperationLocked(PinnedLockedOperations.Shortcuts)) return;
            var action = BuiltinShortcuts.Resolve(BuiltinShortcuts.Snapshot(), "BuiltinShortcutPinnedContext", args);
            if (ParseTransformAction(action) is not { } transform) return;
            args.Handled = true;
            _ = RunAsync(() => ApplyImageTransformAsync(transform, _lifetime.Token));
        }

        private bool CanTransformImage(PinnedImageTransform transform)
        {
            return !_closed && !_busy && _annotation is null && !IsMouseThrough &&
                   !IsOperationLocked(PinnedLockedOperations.Annotation) &&
                   (transform is PinnedImageTransform.Grayscale or PinnedImageTransform.Invert ||
                    !IsOperationLocked(PinnedLockedOperations.Resize));
        }

        private async Task ApplyImageTransformAsync(PinnedImageTransform transform, CancellationToken token = default)
        {
            if (!Enum.IsDefined(transform)) throw new ArgumentOutOfRangeException(nameof(transform));
            token.ThrowIfCancellationRequested();
            if (_closed || _annotation is not null || IsMouseThrough ||
                IsOperationLocked(PinnedLockedOperations.Annotation) ||
                (transform is not (PinnedImageTransform.Grayscale or PinnedImageTransform.Invert) &&
                 IsOperationLocked(PinnedLockedOperations.Resize))) return;
            using var work = token.CanBeCanceled && token != _lifetime.Token
                ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token)
                : null;
            var workToken = work?.Token ?? _lifetime.Token;
            var source = _png;
            var sourceHdr = _hdrSource;
            var width = (int)_bitmap.Size.Width;
            var height = (int)_bitmap.Size.Height;
            var prepared = await PrepareTransformAsync(source, sourceHdr, transform, workToken);
            workToken.ThrowIfCancellationRequested();
            if (_closed || !ReferenceEquals(source, _png) || !ReferenceEquals(sourceHdr, _hdrSource) ||
                _annotation is not null) return;
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

            if (workToken.IsCancellationRequested || _closed || !ReferenceEquals(source, _png))
            {
                bitmap.Dispose();
                thumbnail.Dispose();
                workToken.ThrowIfCancellationRequested();
                return;
            }

            var previousBitmap = _bitmap;
            var previousThumbnail = _thumbnail;
            _bitmap = bitmap;
            _thumbnail = thumbnail;
            _croppedImage = null;
            _png = prepared.Png;
            _pixels = prepared.Pixels;
            _sdrPng = prepared.SdrPng;
            ContentHash = prepared.ContentHash;
            Id = Guid.NewGuid().ToString("N");
            if (_thumbnailRegion is { } region) _thumbnailRegion = TransformRegion(region, width, height, transform);
            _regionSelectionMode = false;
            if (_regionOutline is not null) _content.Children.Remove(_regionOutline);
            _viewport.Offset = default;
            ResetTextRecognition();
            ConfigureHdr(prepared.Hdr);
            UpdateSize();
            RestoreToScreen();
            previousBitmap.Dispose();
            previousThumbnail.Dispose();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private static async Task<PinnedImageContent> PrepareTransformAsync(byte[] png, CapturedFrame? hdr,
            PinnedImageTransform transform, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(png);
            if (!Enum.IsDefined(transform)) throw new ArgumentOutOfRangeException(nameof(transform));
            token.ThrowIfCancellationRequested();
            if (hdr is null)
                return await Task.Run(() => PinnedImageContent.Decode(TransformPng(png, transform, token), token),
                    token);
            var transformed = await Task.Run(() =>
                transform is PinnedImageTransform.Grayscale or PinnedImageTransform.Invert
                    ? HdrPixelEffectProcessor.Apply(hdr,
                    [
                        new(transform == PinnedImageTransform.Grayscale
                            ? PixelEffectKind.Grayscale
                            : PixelEffectKind.Invert),
                    ], token)
                    : TransformHdr(hdr, transform, token), token);
            var encoded = await HdrImageService.EncodePngAsync(transformed, token);
            if (encoded.Length > PinnedImageContent.MaximumEncodedBytes)
                throw new ArgumentException(UiText.Get("RecordingTooLarge"), nameof(hdr));
            return await Task.Run(() =>
            {
                var sdr = HdrToneMapping.Map(transformed, token: token);
                using var image = new MagickImage();
                image.ReadPixels(sdr.Pixels.Span,
                    new PixelReadSettings((uint)sdr.Width, (uint)sdr.Height, StorageType.Char, PixelMapping.BGRA));
                image.ColorSpace = ColorSpace.sRGB;
                var preview = PinnedImageContent.Decode(image.ToByteArray(MagickFormat.Png32), token);
                token.ThrowIfCancellationRequested();
                return PinnedImageContent.FromHdr(encoded, preview, transformed);
            }, token);
        }

        private static CapturedFrame TransformHdr(CapturedFrame frame, PinnedImageTransform transform,
            CancellationToken token)
        {
            HdrAnnotationBlender.Validate(frame);
            var width = transform == PinnedImageTransform.RotateClockwise ? frame.Height : frame.Width;
            var height = transform == PinnedImageTransform.RotateClockwise ? frame.Width : frame.Height;
            var pixels = new byte[checked(width * height * 8)];
            for (var y = 0; y < frame.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < frame.Width; x++)
                {
                    if ((x & 4095) == 0) token.ThrowIfCancellationRequested();
                    var point = TransformPoint(x, y, frame.Width, frame.Height, transform);
                    frame.Pixels.Span.Slice(y * frame.Stride + x * 8, 8)
                        .CopyTo(pixels.AsSpan((point.Y * width + point.X) * 8, 8));
                }
            }

            FrameColorRegion[] colors =
            [
                .. frame.ColorRegions.Select(region => region with
                {
                    Region = TransformRegion(region.Region.Intersect(new(0, 0, frame.Width, frame.Height)), frame.Width,
                        frame.Height, transform),
                }).Where(region => !region.Region.IsEmpty),
            ];
            return frame with
            {
                Width = width, Height = height, Stride = checked(width * 8), Pixels = pixels,
                ColorRegions = colors,
            };
        }

        private static PixelPoint TransformPoint(int x, int y, int width, int height, PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => new(height - 1 - y, x),
                PinnedImageTransform.Rotate180 => new(width - 1 - x, height - 1 - y),
                PinnedImageTransform.FlipHorizontal => new(width - 1 - x, y),
                PinnedImageTransform.FlipVertical => new(x, height - 1 - y),
                _ => new(x, y),
            };
        }

        private static PixelRect TransformRegion(PixelRect region, int width, int height,
            PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => new(height - region.Bottom, region.X, region.Height,
                    region.Width),
                PinnedImageTransform.Rotate180 => new(width - region.Right, height - region.Bottom, region.Width,
                    region.Height),
                PinnedImageTransform.FlipHorizontal => region with { X = width - region.Right },
                PinnedImageTransform.FlipVertical => region with { Y = height - region.Bottom },
                _ => region,
            };
        }

        private static string TransformAction(PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => "rotate-clockwise",
                PinnedImageTransform.Rotate180 => "rotate180",
                PinnedImageTransform.FlipHorizontal => "flip-horizontal",
                PinnedImageTransform.FlipVertical => "flip-vertical",
                PinnedImageTransform.Grayscale => "grayscale", PinnedImageTransform.Invert => "invert",
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
        }

        private static PinnedImageTransform? ParseTransformAction(string? action)
        {
            return action switch
            {
                "rotate-clockwise" => PinnedImageTransform.RotateClockwise,
                "rotate180" => PinnedImageTransform.Rotate180,
                "flip-horizontal" => PinnedImageTransform.FlipHorizontal,
                "flip-vertical" => PinnedImageTransform.FlipVertical,
                "grayscale" => PinnedImageTransform.Grayscale, "invert" => PinnedImageTransform.Invert, _ => null,
            };
        }

        private static string TransformLabel(PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => "PinnedImageRotateClockwise",
                PinnedImageTransform.Rotate180 => "PinnedImageRotate180",
                PinnedImageTransform.FlipHorizontal => "AnimationFlipHorizontal",
                PinnedImageTransform.FlipVertical => "AnimationFlipVertical",
                PinnedImageTransform.Grayscale => "AnimationGrayscale",
                PinnedImageTransform.Invert => "AnimationInvert",
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
        }
    }
}
