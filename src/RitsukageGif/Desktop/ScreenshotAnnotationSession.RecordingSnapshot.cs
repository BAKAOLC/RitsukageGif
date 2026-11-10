using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private static readonly IReadOnlyList<(string Type, ReadOnlyMemory<byte> Data)> RecordingSdrMetadata =
            [("sRGB", new byte[] { 0 })];

        internal RecordingAnnotationSnapshot CreateRecordingSnapshot(PixelSize nativeSize)
        {
            Dispatcher.UIThread.VerifyAccess();
            var layout = new ImagePreviewLayout(new(Selection.X, Selection.Y, Selection.Width, Selection.Height),
                Selection, nativeSize);
            var annotations = CaptureHighPrecisionAnnotations(true);
            if (AnnotationsVisible && IsCanvasTextEditing && !IsNumberCaptionEditing &&
                !string.IsNullOrWhiteSpace(TextEditValue))
            {
                var text = new ScreenshotAnnotation
                {
                    Tool = ScreenshotTool.Text, Text = TextEditValue, Color = Color, Thickness = Thickness,
                    FontSize = FontSize, MosaicSize = MosaicSize, Properties = Properties,
                };
                text.Points.Add(TextEditPosition);
                annotations.Add(text);
            }

            var layers = new List<RecordingAnnotationLayerSnapshot>();
            try
            {
                foreach (var annotation in annotations)
                {
                    var tiles = new List<RecordingAnnotationTile>();
                    if (annotation.Tool is not (ScreenshotTool.Highlight or ScreenshotTool.Mosaic or
                        ScreenshotTool.Blur or ScreenshotTool.SmartErase or ScreenshotTool.Eraser))
                        CaptureRecordingTiles(annotation, layout, tiles);
                    layers.Add(new(ImagePreviewPixelAnnotation(annotation, layout), [.. tiles]));
                }
            }
            finally
            {
                foreach (var annotation in annotations) annotation.Dispose();
            }

            return new(nativeSize, [.. layers]);
        }

        internal Task<CapturedFrame> ComposeRecordingSnapshotAsync(RecordingAnnotationSnapshot snapshot,
            CapturedFrame source, byte[]? working, byte[]? original, CancellationToken token)
        {
            Selection = new(0, 0, source.Width, source.Height);
            return source.PixelFormat == CapturePixelFormat.Bgra8Srgb
                ? ComposeRecordingSdrAsync(source, snapshot.Layers, working!, original, token)
                : ComposeRecordingHdrAsync(source, snapshot.Layers, token);
        }

        private void CaptureRecordingTiles(ScreenshotAnnotation annotation, ImagePreviewLayout layout,
            List<RecordingAnnotationTile> tiles)
        {
            var affected = RenderBounds(annotation).TransformToAABB(layout.Transform).Inflate(1);
            const int tileSize = 1024;
            for (var y = 0; y < layout.WorkSize.Height; y += tileSize)
            for (var x = 0; x < layout.WorkSize.Width; x += tileSize)
            {
                var area = new PixelRect(x, y, Math.Min(tileSize, layout.WorkSize.Width - x),
                    Math.Min(tileSize, layout.WorkSize.Height - y));
                if (!affected.Intersects(new(x, y, area.Width, area.Height))) continue;
                using var tile = new RenderTargetBitmap(new(area.Width, area.Height), new(96, 96));
                using (var drawing = tile.CreateDrawingContext())
                {
                    using var transform = drawing.PushTransform(new(layout.ScaleX, 0, 0, layout.ScaleY,
                        -layout.Sampling.X * layout.ScaleX - area.X, -layout.Sampling.Y * layout.ScaleY - area.Y));
                    using var clip = drawing.PushClip(new(AnnotationBounds.X, AnnotationBounds.Y,
                        AnnotationBounds.Width, AnnotationBounds.Height));
                    Draw(drawing, annotation, false);
                }

                using var framebuffer = new ImagePreviewFramebuffer(new(area.Width, area.Height));
                tile.CopyPixels(framebuffer);
                var rgba16 = PngColorTransform.OverlayToSource(framebuffer.Pixels, area.Width, area.Height,
                    RecordingSdrMetadata, CancellationToken.None);
                tiles.Add(new(area, framebuffer.Pixels, rgba16));
            }
        }

        private async Task<CapturedFrame> ComposeRecordingSdrAsync(CapturedFrame source,
            IReadOnlyList<RecordingAnnotationLayerSnapshot> layers, byte[] output, byte[]? originals,
            CancellationToken token)
        {
            ValidateNativeSdr(source);
            WriteRecordingSdrSamples(source, originals ?? output, token);
            originals?.CopyTo(output, 0);
            var original = new PngRaster(source.Width, source.Height, 16, 6, originals ?? output,
                RecordingSdrMetadata);
            foreach (var (annotation, tiles) in layers)
            {
                token.ThrowIfCancellationRequested();
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (annotation.Tool)
                {
                    case ScreenshotTool.Highlight:
                        await ApplyHighPrecisionMarkerAsync(output, original, Selection, annotation, token)
                            .ConfigureAwait(false);
                        continue;
                    case ScreenshotTool.Mosaic:
                    case ScreenshotTool.Blur:
                        ApplyPixelLayer16(output, original, Selection, annotation, token);
                        continue;
                    case ScreenshotTool.SmartErase:
                        ApplyInpaintingLayer16(output, original, Selection, annotation,
                            annotation.Properties.InpaintingRadius, token);
                        continue;
                    case ScreenshotTool.Eraser:
                        await RestoreHighPrecisionAsync(output, original, Selection, annotation, token)
                            .ConfigureAwait(false);
                        continue;
                    case ScreenshotTool.Magnifier:
                        NativeDensityMagnifySdr(output, original, original, Selection, annotation, token);
                        break;
                }

                foreach (var tile in tiles)
                    Png16PixelProcessor.Blend(output, original.Width, original.Height, tile.Area, tile.Rgba16, token);
            }

            var result = new byte[checked(source.Width * source.Height * 4)];
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < source.Width; x++)
                {
                    var input = output.AsSpan((y * source.Width + x) * 8, 8);
                    var target = result.AsSpan((y * source.Width + x) * 4, 4);
                    for (var channel = 0; channel < 3; channel++)
                        target[2 - channel] =
                            (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[(channel * 2)..]) + 128) / 257);
                    target[3] = (byte)((BinaryPrimitives.ReadUInt16BigEndian(input[6..]) + 128) / 257);
                }
            }

            return source with
            {
                Pixels = result, Stride = source.Width * 4,
                AlphaMode = source.AlphaMode == CaptureAlphaMode.Opaque
                    ? CaptureAlphaMode.Opaque
                    : CaptureAlphaMode.Straight,
            };
        }

        private async Task<CapturedFrame> ComposeRecordingHdrAsync(CapturedFrame source,
            IReadOnlyList<RecordingAnnotationLayerSnapshot> layers,
            CancellationToken token)
        {
            HdrAnnotationBlender.Validate(source);
            var output = source.Pixels[..checked(source.Stride * source.Height)].ToArray();
            foreach (var (annotation, tiles) in layers)
            {
                token.ThrowIfCancellationRequested();
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (annotation.Tool)
                {
                    case ScreenshotTool.Highlight:
                        await ApplyHdrMarkerAsync(output, source, Selection, annotation, token).ConfigureAwait(false);
                        continue;
                    case ScreenshotTool.Mosaic:
                    case ScreenshotTool.Blur:
                        ApplyPixelLayerHdr(output, source, Selection, annotation, token);
                        continue;
                    case ScreenshotTool.SmartErase:
                        ApplyInpaintingLayerHdr(output, source, Selection, annotation,
                            annotation.Properties.InpaintingRadius, token);
                        continue;
                    case ScreenshotTool.Eraser:
                    case ScreenshotTool.Magnifier:
                        RestoreHdrSamples(output, source, Selection, source, Selection, annotation, token);
                        if (annotation.Tool == ScreenshotTool.Eraser) continue;
                        break;
                }

                foreach (var tile in tiles)
                    HdrAnnotationBlender.Blend(output, source, tile.Area, tile.Bgra, tile.Area.Width * 4,
                        CaptureAlphaMode.Straight, token);
            }

            return source with { Pixels = output };
        }

        private static void WriteRecordingSdrSamples(CapturedFrame source, byte[] destination,
            CancellationToken token)
        {
            for (var y = 0; y < source.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                var row = source.Pixels.Span.Slice(y * source.Stride, source.Width * 4);
                for (var x = 0; x < source.Width; x++)
                {
                    var pixel = row.Slice(x * 4, 4);
                    var alpha = source.AlphaMode == CaptureAlphaMode.Opaque ? 255 : pixel[3];
                    var target = destination.AsSpan((y * source.Width + x) * 8, 8);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var value = source.AlphaMode == CaptureAlphaMode.Premultiplied
                            ? alpha == 0 ? 0 : Math.Min(255, (pixel[2 - channel] * 255 + alpha / 2) / alpha)
                            : pixel[2 - channel];
                        BinaryPrimitives.WriteUInt16BigEndian(target[(channel * 2)..], (ushort)(value * 257));
                    }

                    BinaryPrimitives.WriteUInt16BigEndian(target[6..], (ushort)(alpha * 257));
                }
            }
        }
    }
}
