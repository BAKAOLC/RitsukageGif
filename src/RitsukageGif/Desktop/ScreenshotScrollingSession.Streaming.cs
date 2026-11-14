namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private (PixelRect Area, byte[]? Sdr, CapturedFrame? Hdr)? _streamingAnnotations;

        internal bool RequiresStreamingExport
        {
            get
            {
                lock (_sync) return _stitching.RequiresStreamingExport;
            }
        }

        internal (int Width, int Height) StreamingSize
        {
            get
            {
                lock (_sync) return (_stitching.Width, _stitching.Height);
            }
        }

        internal async Task PrepareStreamingAnnotationsAsync(ScreenshotAnnotationSession annotations,
            OutputColorMode mode, CancellationToken token)
        {
            ScrollCaptureSession source;
            (PixelRect Area, byte[] Pixels)? cursor;
            lock (_sync)
            {
                source = _stitching;
                cursor = CursorOverlay(source.Width, source.Height);
            }

            var area = annotations.StreamingAnnotationArea(new(0, 0, source.Width, source.Height));
            if (area.IsEmpty) return;
            byte[]? sdr = null;
            CapturedFrame? hdr = null;
            if (mode != OutputColorMode.Hdr)
            {
                var pixels = await Task.Run(() =>
                {
                    var result = source.ReadOutputSdrRegion(area, token);
                    for (var y = 0; y < area.Height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        var row = result.AsSpan(y * area.Width * 4, area.Width * 4);
                        ApplyStreamingCursor(row, area.X, area.Y + y, cursor);
                    }

                    return result;
                }, token);
                sdr = await annotations.ComposeStreamingSdrAsync(area, pixels, token);
            }

            if (mode != OutputColorMode.Sdr)
            {
                var captured = await Task.Run(() => source.ReadOutputHdrRegion(area, token), token);
                if (cursor is { } overlay) captured = BlendStreamingHdrCursor(captured, area, overlay, token);

                hdr = await annotations.ComposeHdrAsync(area, captured, token: token);
            }

            _streamingAnnotations = (area, sdr, hdr);
        }

        internal void ClearStreamingAnnotations()
        {
            _streamingAnnotations = null;
        }

        internal void WriteStreamingPng(Stream stream, int width, int height, bool hdr,
            CaptureDecoration decoration, CancellationToken token)
        {
            // The owning window pauses capture and drains output before disposing the session.
            ScrollCaptureSession source;
            (PixelRect Area, byte[] Pixels)? cursor;
            var annotations = _streamingAnnotations;
            lock (_sync)
            {
                source = _stitching;
                cursor = CursorOverlay(source.Width, source.Height);
            }

            var hdrWhite = hdr ? source.OutputHdrColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits : 1;
            var hdrCursor = hdr && cursor is { } overlay
                ? BlendStreamingHdrCursor(source.ReadOutputHdrRegion(overlay.Area, token), overlay.Area, overlay, token)
                : null;
            using var decorated = decoration.IsEmpty
                ? null
                : CaptureDecorationProcessor.CreateStreamingRows(source.Width, source.Height, decoration, ReadRow, hdr,
                    hdrWhite);
            var sourceWidth = decorated?.Geometry.Width ?? source.Width;
            var sourceHeight = decorated?.Geometry.Height ?? source.Height;
            StaticPngRowReader rows = decorated is null ? ReadRow : decorated.ReadRow;
            using var resampler = width == sourceWidth && height == sourceHeight
                ? null
                : new StaticPngRowResampler(sourceWidth, sourceHeight, width, height, rows, hdr);
            StaticPngStreamWriter.Write(stream, width, height, resampler is null ? rows : resampler.ReadRow, token,
                hdr ? CaptureAlphaMode.Premultiplied : null);
            return;

            void ReadRow(int y, Span<byte> row, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                source.CopyOutputRow(y, row, hdr, cancellation);
                if (!hdr)
                    ApplyStreamingCursor(row, 0, y, cursor);
                else if (hdrCursor is not null && cursor is { } hdrOverlay && y >= hdrOverlay.Area.Y &&
                         y < hdrOverlay.Area.Bottom)
                    hdrCursor.Pixels.Span.Slice((y - hdrOverlay.Area.Y) * hdrCursor.Stride, hdrCursor.Stride)
                        .CopyTo(row.Slice(hdrOverlay.Area.X * 8, hdrCursor.Stride));
                if (annotations is not { } patch || y < patch.Area.Y || y >= patch.Area.Bottom) return;
                var bytes = hdr ? 8 : 4;
                var patchPixels = hdr ? patch.Hdr?.Pixels ?? default : patch.Sdr;
                if (!patchPixels.IsEmpty)
                    patchPixels.Span.Slice((y - patch.Area.Y) * patch.Area.Width * bytes, patch.Area.Width * bytes)
                        .CopyTo(row.Slice(patch.Area.X * bytes, patch.Area.Width * bytes));
            }
        }

        private static void ApplyStreamingCursor(Span<byte> row, int left, int y,
            (PixelRect Area, byte[] Pixels)? cursor)
        {
            if (cursor is not { } overlay || y < overlay.Area.Y || y >= overlay.Area.Bottom) return;
            var start = Math.Max(left, overlay.Area.X);
            var end = Math.Min(left + row.Length / 4, overlay.Area.Right);
            if (end <= start) return;
            var pixels = overlay.Pixels.AsSpan(((y - overlay.Area.Y) * overlay.Area.Width + start - overlay.Area.X) * 4,
                (end - start) * 4);
            BlendCursorRow(row.Slice((start - left) * 4, (end - start) * 4), pixels);
        }

        private static CapturedFrame BlendStreamingHdrCursor(CapturedFrame frame, PixelRect area,
            (PixelRect Area, byte[] Pixels) overlay, CancellationToken token)
        {
            var overlap = overlay.Area.Intersect(area);
            if (overlap.IsEmpty) return frame;
            var pixels = overlay.Pixels;
            if (overlap != overlay.Area)
            {
                pixels = new byte[checked(overlap.Width * overlap.Height * 4)];
                for (var y = 0; y < overlap.Height; y++)
                    overlay.Pixels.AsSpan(((overlap.Y - overlay.Area.Y + y) * overlay.Area.Width + overlap.X -
                                           overlay.Area.X) * 4, overlap.Width * 4)
                        .CopyTo(pixels.AsSpan(y * overlap.Width * 4));
            }

            var output = frame.Pixels.ToArray();
            HdrAnnotationBlender.Blend(output, frame,
                new(overlap.X - area.X, overlap.Y - area.Y, overlap.Width, overlap.Height),
                pixels, overlap.Width * 4, CaptureAlphaMode.Straight, token);
            return frame with { Pixels = output };
        }
    }
}
