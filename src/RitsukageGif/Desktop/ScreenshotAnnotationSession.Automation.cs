namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal int AutomationCount => _annotations.Count;

        internal async Task<int> WriteAutomationAnnotationAsync(ScreenshotAnnotationRecord record,
            byte[]? watermark, CancellationToken token, int? replaceIndex = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (IsBusy || IsDrawing) throw new InvalidOperationException("The annotation canvas is busy.");
            if (record.Tool is ScreenshotTool.Select or ScreenshotTool.AutoMosaic or ScreenshotTool.StrokeEraser)
                throw new ArgumentException("This operation is not a stored annotation tool.", nameof(record));
            if (replaceIndex is { } index)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _annotations.Count);
            }

            ValidateDocument(new() { Layers = [record], Current = [0] });
            if ((replaceIndex is null && _annotations.Count >= 1024) || record.Points.LongLength +
                _annotations.Concat(_retained).Distinct().Sum(item => (long)item.Points.Count) > 262144)
                throw new InvalidOperationException(UiText.Get("ScreenshotAnnotationLimit"));
            ScreenshotAnnotationImage? image = null;
            if (watermark is not null)
            {
                if (record.Tool != ScreenshotTool.Watermark)
                    throw new ArgumentException("An image resource requires the watermark tool.", nameof(watermark));
                ValidateWatermarkImage(watermark);
                using var stream = new MemoryStream(watermark, false);
                image = new(new(stream), watermark);
            }

            var annotation = new ScreenshotAnnotation
            {
                Tool = record.Tool, Color = record.Color, Thickness = record.Thickness,
                FontSize = record.FontSize, MosaicSize = record.MosaicSize, Text = record.Text,
                Properties = record.Properties, AutoMosaicId = record.AutoMosaicId,
                AutoMosaicRegions = [.. record.AutoMosaicRegions.Select(region => region.Bounds)],
                AutoMosaicCandidates = [.. record.AutoMosaicCandidates.Select(candidate => candidate!)],
                AutoMosaicCandidateBounds = record.AutoMosaicCandidateBounds.Bounds,
                MagnifierOffset = new(record.MagnifierOffset.X, record.MagnifierOffset.Y),
                WatermarkImage = image, WatermarkTimestamp = record.WatermarkTimestamp == default
                    ? DateTimeOffset.Now
                    : record.WatermarkTimestamp,
            };
            annotation.Points.AddRange(record.Points.Select(point => point.Point));
            var committed = false;
            IsBusy = true;
            try
            {
                if (annotation.Tool is ScreenshotTool.Mosaic or ScreenshotTool.Blur or ScreenshotTool.SmartErase
                    or ScreenshotTool.Magnifier or ScreenshotTool.Eraser)
                    annotation.ImageTiles = await CreateImageEffectAsync(annotation, token, replaceIndex);
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (replaceIndex is { } target)
                {
                    NormalizeGeometryPoints(annotation);
                    SaveHistory();
                    _retained.Add(annotation);
                    _annotations[target] = annotation;
                    _selected = null;
                    Error = null;
                }
                else
                {
                    Commit(annotation);
                }

                committed = true;
                return replaceIndex ?? _annotations.Count - 1;
            }
            finally
            {
                if (!committed) annotation.Dispose();
                IsBusy = false;
                Changed?.Invoke();
            }
        }

        internal Task<int> MoveAutomationAnnotationAsync(int index, double dx, double dy, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _annotations.Count);
            var annotation = _annotations[index];
            var record = annotation.CaptureDocumentRecord();
            return WriteAutomationAnnotationAsync(record with
            {
                Points = [.. record.Points.Select(point => new AnnotationDocumentPoint(point.X + dx, point.Y + dy))],
            }, annotation.WatermarkImage?.GetEncoded(), token, index);
        }

        internal int EraseAutomationStrokes(IReadOnlyList<AnnotationDocumentPoint> points, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (IsBusy || IsDrawing) throw new InvalidOperationException("The annotation canvas is busy.");
            if (points.Count == 0) return 0;
            var before = _annotations.Count;
            _eraseOriginal = [.. _annotations];
            _gesture = new() { Tool = ScreenshotTool.StrokeEraser, Color = Color, Thickness = Thickness };
            var first = Pixel(points[0]);
            _gesture.Points.Add(new(first.X, first.Y));
            try
            {
                EraseAt(_gesture.Points[0]);
                foreach (var point in points.Skip(1))
                {
                    token.ThrowIfCancellationRequested();
                    Move(Pixel(point));
                }

                token.ThrowIfCancellationRequested();
                FinishErase();
                return before - _annotations.Count;
            }
            finally
            {
                CancelErase();
                _gesture.Dispose();
                _gesture = null;
            }

            PixelPoint Pixel(AnnotationDocumentPoint point)
            {
                return new(
                    (int)Math.Round(Math.Clamp(point.X, AnnotationBounds.X, AnnotationBounds.Right)),
                    (int)Math.Round(Math.Clamp(point.Y, AnnotationBounds.Y, AnnotationBounds.Bottom)));
            }
        }

        internal void RemoveAutomationAnnotation(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsBusy || IsDrawing) throw new InvalidOperationException("The annotation canvas is busy.");
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _annotations.Count);
            SaveHistory();
            _annotations.RemoveAt(index);
            _selected = null;
            SynchronizeNumberCounter();
            Changed?.Invoke();
        }
    }
}
