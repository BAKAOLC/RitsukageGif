namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private bool _frameAlphaKnown;
        private bool _frameAlphaOpaque;
        private byte[]? _frameAlphaSource;
        private (PixelRect Region, CaptureDecoration Decoration, byte[]? Source)? _frameShadowFailed;
        private (PixelRect Region, CaptureDecoration Decoration, byte[]? Source)? _frameShadowKey;
        private SelectionDecorationPreview? _frameShadowPreview;
        private (PixelRect Region, CaptureDecoration Decoration, byte[]? Source)? _frameShadowRequested;
        private bool _frameShadowRunning;
        private CancellationTokenSource? _frameShadowWork;

        internal SelectionDecorationPreview? FrameShadowPreview => _frameShadowKey == FrameShadowKey()
            ? _frameShadowPreview
            : null;

        private (PixelRect Region, CaptureDecoration Decoration, byte[]? Source)? FrameShadowKey()
        {
            var region = Snapshot.Region;
            var decoration = Decoration;
            if (!ScreenshotMode || region.IsEmpty || decoration.Kind != CaptureDecorationKind.Shadow ||
                Snapshot.Phase is SelectionPhase.Candidate or SelectionPhase.Pressed) return null;
            var source = WindowSource ? _frozenWindow?.Pixels : _screenshots.FirstOrDefault().Pixels;
            if (_frameAlphaKnown && _frameAlphaOpaque && ReferenceEquals(source, _frameAlphaSource))
                region = new(0, 0, region.Width, region.Height);
            return (region, decoration, source);
        }

        private void UpdateFrameDecorationPreview()
        {
            var key = FrameShadowKey();
            if (key == _frameShadowRequested || key == _frameShadowKey || key == _frameShadowFailed) return;
            _frameShadowRequested = key;
            _frameShadowWork?.Cancel();
            if (key is null || _frameShadowRunning) return;
            var frames = WindowSource && _frozenWindow is { } window
                ? new[] { (window.Region, window.Pixels) }
                : _screenshots.ToArray();
            _frameShadowRunning = true;
            _ = BuildFrameShadowAsync(key.Value, frames);
        }

        private async Task BuildFrameShadowAsync((PixelRect Region, CaptureDecoration Decoration, byte[]? Source) key,
            IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames)
        {
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _frameShadowWork = work;
            SelectionDecorationPreview? preview = null;
            try
            {
                var knownAlpha = _frameAlphaKnown && ReferenceEquals(key.Source, _frameAlphaSource)
                    ? _frameAlphaOpaque
                    : (bool?)null;
                var generated = await Task.Run(() =>
                {
                    var opaque = knownAlpha ?? OpaqueFrames(frames, work.Token);
                    var mask = CaptureDecorationProcessor.CreateShadowAlpha(key.Region.Width, key.Region.Height,
                        key.Decoration, opaque ? null : ReadAlpha, work.Token);
                    return (Preview: SelectionDecorationPreview.Create(mask, key.Decoration.Color, work.Token),
                        Opaque: opaque);
                }, work.Token);
                preview = generated.Preview;
                if (_finished || work.IsCancellationRequested || _frameShadowRequested != key) return;
                _frameShadowPreview?.Dispose();
                _frameShadowPreview = preview;
                preview = null;
                _frameAlphaSource = key.Source;
                _frameAlphaKnown = true;
                _frameAlphaOpaque = generated.Opaque;
                _frameShadowKey = _frameShadowRequested = FrameShadowKey();
                _frameShadowFailed = null;
                Refresh();
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                _frameShadowFailed = key;
                HistoryError?.Invoke(error);
            }
            finally
            {
                preview?.Dispose();
                _frameShadowRunning = false;
                if (ReferenceEquals(_frameShadowWork, work)) _frameShadowWork = null;
                if (!_finished && _frameShadowRequested != _frameShadowKey &&
                    _frameShadowRequested != _frameShadowFailed)
                {
                    _frameShadowRequested = null;
                    UpdateFrameDecorationPreview();
                }
            }

            return;

            void ReadAlpha(int y, Span<float> alpha)
            {
                alpha.Fill(1);
                var globalY = key.Region.Y + y;
                foreach (var frame in frames)
                {
                    if (globalY < frame.Region.Y || globalY >= frame.Region.Bottom) continue;
                    var left = Math.Max(key.Region.X, frame.Region.X);
                    var right = Math.Min(key.Region.Right, frame.Region.Right);
                    for (var x = left; x < right; x++)
                        alpha[x - key.Region.X] = frame.Pixels[
                            ((globalY - frame.Region.Y) * frame.Region.Width + x - frame.Region.X) * 4 + 3] / 255f;
                }
            }
        }

        private static bool OpaqueFrames(IReadOnlyList<(PixelRect Region, byte[] Pixels)> frames,
            CancellationToken token)
        {
            foreach (var frame in frames)
                for (var row = 0; row < frame.Region.Height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    var end = (row + 1) * frame.Region.Width * 4;
                    for (var index = row * frame.Region.Width * 4 + 3; index < end; index += 4)
                        if (frame.Pixels[index] != byte.MaxValue)
                            return false;
                }

            return true;
        }

        private void DisposeFrameDecorationPreview()
        {
            _frameShadowWork?.Cancel();
            _frameShadowPreview?.Dispose();
            _frameShadowPreview = null;
            _frameAlphaSource = null;
            _frameAlphaKnown = false;
        }
    }
}
