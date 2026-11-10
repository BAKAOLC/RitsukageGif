using Avalonia;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private string CurrentPinShortcutContext => LiveCaptureActive
            ? BuiltinShortcutContexts.PinnedWindowRoi
            : AnimationActive
                ? BuiltinShortcutContexts.PinnedDynamicImage
                : BuiltinShortcutContexts.PinnedImage;

        internal Func<Task>? ConvertLiveRequested { get; set; }
        internal double CurrentPinZoom { get; private set; } = 1;

        private bool CanControlAnimation => AnimationActive && !_closed && !_busy && _annotation is null &&
                                            !IsMouseThrough && _animationPlayback is not null;

        private bool CanConvertLiveToImage => LiveCaptureActive && LiveSourceReady && !_busy &&
                                              _annotation is null && !IsMouseThrough;

        private bool TryExecutePinSubtypeShortcut(string action)
        {
            if (AnimationActive && action.StartsWith("animation-", StringComparison.Ordinal) && !CanControlAnimation)
                return true;
            if (LiveCaptureActive && action.StartsWith("live-", StringComparison.Ordinal) &&
                (_busy || _annotation is not null || IsMouseThrough)) return true;
            if (AnimationActive)
                switch (action)
                {
                    case "animation-toggle":
                        if (_animationPlayback is { Playing: true })
                        {
                            _animationPlayback.Pause();
                            _animationVisiblePlaying = false;
                            _animationTimer?.Stop();
                        }
                        else
                        {
                            StartAnimation();
                        }

                        return true;
                    case "animation-next":
                        _ = RunAsync(() => StepAnimation(1));
                        return true;
                    case "animation-previous":
                        _ = RunAsync(() => StepAnimation(-1));
                        return true;
                    case "animation-speed-up":
                        ChangeAnimationSpeed(1.5);
                        return true;
                    case "animation-speed-down":
                        ChangeAnimationSpeed(1 / 1.5);
                        return true;
                    case "animation-speed-reset":
                        _animationPlayback?.SetSpeed(1);
                        TickAnimation();
                        return true;
                }

            if (!LiveCaptureActive) return false;
            switch (action)
            {
                case "live-convert":
                    _ = RunAsync(ConvertLiveToImageAsync);
                    return true;
                case "live-rotate-clockwise":
                    ExecuteLiveTransform(PinnedImageTransform.RotateClockwise);
                    return true;
                case "live-rotate-counterclockwise":
                    ExecuteLiveTransform(PinnedImageTransform.RotateCounterclockwise);
                    return true;
                case "live-flip-horizontal":
                    ExecuteLiveTransform(PinnedImageTransform.FlipHorizontal);
                    return true;
                case "live-flip-vertical":
                    ExecuteLiveTransform(PinnedImageTransform.FlipVertical);
                    return true;
                case "live-reset":
                    if (CanTransformLiveImage() && !IsOperationLocked(PinnedLockRestrictions.Move))
                    {
                        _viewTransform = default;
                        CurrentPinZoom = 1;
                        Opacity = CurrentPreferences.Opacity;
                        _viewport.Offset = default;
                        CancelThumbnailLayout();
                        ClearTextSelection();
                        UpdateSize();
                        RefreshHdrPreview();
                        RestoreToScreen();
                        StateChanged?.Invoke(this, EventArgs.Empty);
                    }

                    return true;
                default: return false;
            }
        }

        private void ExecuteLiveTransform(PinnedImageTransform transform)
        {
            if (CanTransformImage(transform))
                _ = RunAsync(() => ApplyImageTransformAsync(transform, _lifetime.Token));
        }

        private void ChangeAnimationSpeed(double factor)
        {
            if (_animationPlayback is not { } playback) return;
            playback.SetSpeed(Math.Clamp(playback.Speed * factor, .1, 8));
            TickAnimation();
        }

        private Task ConvertLiveToImageAsync()
        {
            return LiveCaptureActive && LiveSourceReady
                ? ConvertLiveRequested?.Invoke() ?? Task.CompletedTask
                : Task.CompletedTask;
        }

        internal async Task<ScreenshotResult> CaptureLiveImageConversionAsync(CancellationToken token)
        {
            var transform = _viewTransform;
            var source = await CaptureSceneOutputAsync(OutputColorMode.Both, token);
            if (transform.IsIdentity) return source;
            var dimensions = PngImageCodec.ReadDimensions(source.Png);
            var sourceSize = new Size(dimensions.Width, dimensions.Height);
            var target = transform.ViewSize(sourceSize);
            return await PinnedViewportOutput.ProjectAsync(Task.FromResult(source),
                new(transform.MatrixFor(sourceSize), new((int)target.Width, (int)target.Height),
                    true, 1, false, source.CaptureScaling), token);
        }

        internal void PrepareConvertedView(double zoom, double opacity)
        {
            CurrentPinZoom = zoom;
            Opacity = opacity;
            UpdateSize();
        }
    }
}
