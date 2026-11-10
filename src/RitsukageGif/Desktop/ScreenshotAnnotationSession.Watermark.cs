using Avalonia.Media;
using ImageMagick;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        private bool _automaticWatermarkAttempted;
        private Task _watermarkLoadTask = Task.CompletedTask;
        private CancellationTokenSource? _watermarkLoadWork;
        private string? _watermarkRequestedPath;
        private string? _watermarkSourcePath;
        internal Task AutomaticWatermarkTask { get; private set; } = Task.CompletedTask;
        internal bool IsEditingWatermark => IsEditingText && _textEditWatermarkText;

        private ScreenshotAnnotation? ActiveStyleAnnotation => _selected ?? (Tool == ScreenshotTool.Watermark
            ? _annotations.FirstOrDefault(annotation => annotation is
                { Tool: ScreenshotTool.Watermark, WatermarkImage: null })
            : null);

        private bool WatermarkResourceReady => Properties.WatermarkImagePath is not { Length: > 0 } path ||
                                               (_watermarkSource is not null && _watermarkSourcePath == path);

        internal void SetWatermarkTimeFormat(string format)
        {
            Properties = Properties with { WatermarkTimeFormat = format };
            if (_textEditPreviousStyle is { } previous)
                _textEditPreviousStyle = previous with
                {
                    Properties = previous.Properties with { WatermarkTimeFormat = format },
                };
            StoreToolStyle(ScreenshotTool.Watermark);
        }

        private void EnsureManualWatermark()
        {
            if (Tool != ScreenshotTool.Watermark || Selection.IsEmpty || string.IsNullOrEmpty(Text) ||
                _watermarkSource is not null ||
                _annotations.Any(annotation => annotation.Tool == ScreenshotTool.Watermark)) return;
            var annotation = new ScreenshotAnnotation
            {
                Tool = ScreenshotTool.Watermark, Text = Text, Color = Color, Thickness = Thickness,
                FontSize = FontSize, MosaicSize = MosaicSize, Properties = Properties.Normalize(),
                WatermarkTimestamp = DateTimeOffset.Now,
            };
            annotation.Points.Add(new(AnnotationBounds.X, AnnotationBounds.Y));
            Commit(annotation);
            _selected = null;
        }

        private void RestoreWatermarkStyle(ScreenshotTool tool)
        {
            if (tool != ScreenshotTool.Watermark) return;
            Text = Properties.WatermarkText;
            if (Properties.WatermarkImagePath is { Length: > 0 } path)
            {
                _ = PrepareWatermarkSourceAsync(path, _cancel.Token);
            }
            else
            {
                _watermarkLoadWork?.Cancel();
                _watermarkSource?.Dispose();
                _watermarkSource = null;
                _watermarkSourcePath = null;
            }
        }

        private void LoadWatermarkObjectStyle(ScreenshotAnnotation annotation)
        {
            if (annotation.Tool != ScreenshotTool.Watermark) return;
            _watermarkLoadWork?.Cancel();
            _watermarkSource?.Dispose();
            _watermarkSource = annotation.WatermarkImage?.Retain();
            _watermarkSourcePath = annotation.Properties.WatermarkImagePath;
            _watermarkRequestedPath = null;
            Text = annotation.Text;
            Properties = Properties with { WatermarkText = annotation.Text };
        }

        private Task PrepareWatermarkSourceAsync(string path, CancellationToken token)
        {
            if (_watermarkSource is not null && _watermarkSourcePath == path) return Task.CompletedTask;
            if (_watermarkRequestedPath == path && !_watermarkLoadTask.IsCompleted) return _watermarkLoadTask;
            _watermarkLoadWork?.Cancel();
            var work = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel.Token);
            _watermarkLoadWork = work;
            _watermarkRequestedPath = path;
            return _watermarkLoadTask = LoadWatermarkSourceAsync(path, work);
        }

        private async Task LoadWatermarkSourceAsync(string path, CancellationTokenSource work)
        {
            IsBusy = true;
            Error = null;
            Changed?.Invoke();
            try
            {
                using var resource = await MediaResource.OpenAsync(path, work.Token);
                if (new FileInfo(resource.Path).Length > 16 * 1024 * 1024)
                    throw new InvalidOperationException(UiText.Get("AnnotationWatermarkTooLarge"));
                var encoded = await File.ReadAllBytesAsync(resource.Path, work.Token);
                ValidateWatermarkImage(encoded);
                using var stream = new MemoryStream(encoded, false);
                var image = new ScreenshotAnnotationImage(new(stream), encoded);
                if (_disposed || work.IsCancellationRequested || !ReferenceEquals(_watermarkLoadWork, work))
                {
                    image.Dispose();
                    return;
                }

                _watermarkSource?.Dispose();
                _watermarkSource = image;
                _watermarkSourcePath = path;
                Logger.ZLogDebug($"Watermark image loaded. SessionId={_logSessionId} Bytes={encoded.Length}");
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Watermark image loading failed. SessionId={_logSessionId}");
                if (!_disposed && ReferenceEquals(_watermarkLoadWork, work)) Error = error.Message;
            }
            finally
            {
                if (ReferenceEquals(_watermarkLoadWork, work))
                {
                    _watermarkLoadWork = null;
                    _watermarkRequestedPath = null;
                    if (!_disposed)
                    {
                        IsBusy = false;
                        Changed?.Invoke();
                    }
                }

                work.Dispose();
            }
        }

        internal void AddAutomaticWatermark()
        {
            if (_automaticWatermarkAttempted || Selection.IsEmpty || _disposed) return;
            _automaticWatermarkAttempted = true;
            if (!_behavior.AutoAddWatermark || _annotations.Any(item => item.Tool == ScreenshotTool.Watermark)) return;
            var style = StyleFor(ScreenshotTool.Watermark).Normalize();
            if (string.IsNullOrWhiteSpace(style.Properties.WatermarkText) &&
                string.IsNullOrEmpty(style.Properties.WatermarkImagePath)) return;
            AutomaticWatermarkTask = AddAutomaticWatermarkAsync(style, DateTimeOffset.Now);
        }

        private async Task AddAutomaticWatermarkAsync(AnnotationToolStyle style, DateTimeOffset timestamp)
        {
            try
            {
                if (style.Properties.WatermarkImagePath is { Length: > 0 } path)
                {
                    await PrepareWatermarkSourceAsync(path, _cancel.Token);
                    if (_watermarkSource is null || _watermarkSourcePath != path) return;
                }

                if (_disposed || _cancel.IsCancellationRequested || Selection.IsEmpty ||
                    _annotations.Any(item => item.Tool == ScreenshotTool.Watermark)) return;
                var annotation = new ScreenshotAnnotation
                {
                    Tool = ScreenshotTool.Watermark, Text = style.Properties.WatermarkText,
                    Color = _sharedColor ? _masterColor : Color.Parse(style.Color), Thickness = style.Thickness,
                    FontSize = style.FontSize, MosaicSize = style.MosaicSize, Properties = style.Properties,
                    WatermarkImage = string.IsNullOrEmpty(style.Properties.WatermarkImagePath)
                        ? null
                        : _watermarkSource?.Retain(),
                    WatermarkTimestamp = timestamp,
                };
                annotation.Points.Add(new(Selection.X, Selection.Y));
                var selected = _selected;
                Commit(annotation);
                _selected = selected;
                Logger.ZLogInformation(
                    $"Automatic screenshot watermark added. SessionId={_logSessionId} Image={annotation.WatermarkImage is not null}");
                Changed?.Invoke();
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Logger.ZLogWarning(error, $"Automatic screenshot watermark failed. SessionId={_logSessionId}");
                if (!_disposed)
                {
                    Error = error.Message;
                    Changed?.Invoke();
                }
            }
        }

        private void ValidateWatermarkImage(byte[] image)
        {
            var retainedBytes = _retained.Select(mark => mark.WatermarkImage)
                .Where(value => value is not null).Distinct()
                .Sum(value => (long)value!.Bitmap.PixelSize.Width * value.Bitmap.PixelSize.Height * 4 +
                              (value.Encoded?.Length ?? 0));
            ValidateWatermarkImage(image, retainedBytes);
        }

        private static long ValidateWatermarkImage(byte[] image, long retainedBytes)
        {
            if (image.Length > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(image));
            var info = new MagickImageInfo(image);
            if (info.Width == 0 || info.Height == 0 || (long)info.Width * info.Height > 16L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(image));
            var storage = (long)info.Width * info.Height * 4 + image.Length;
            if (retainedBytes + storage > 128L * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
            return storage;
        }
    }
}
