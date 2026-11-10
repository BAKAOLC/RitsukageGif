namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private PinPlaybackState GetAutomationPlayback()
        {
            var playback = _animationPlayback ?? throw new NotSupportedException("The pin is not an active animation.");
            return new(playback.Playing, playback.Speed, playback.FrameIndex, AnimationMetadata!.Frames.Count);
        }

        internal PinPlaybackState SetAutomationPlayback(bool? paused, double? speed)
        {
            if (paused is null && speed is null) return GetAutomationPlayback();
            if (!CanControlAnimation)
                throw new InvalidOperationException("The animation cannot be controlled in its current state.");
            if (speed is { } value) _animationPlayback!.SetSpeed(value);
            switch (paused)
            {
                case true:
                    _animationPlayback!.Pause();
                    _animationVisiblePlaying = false;
                    _animationTimer?.Stop();
                    break;
                case false:
                    StartAnimation();
                    break;
            }

            TickAnimation();
            return GetAutomationPlayback();
        }

        internal async Task<PinPlaybackState> StepAutomationAsync(int direction, CancellationToken token)
        {
            if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
            if (!CanControlAnimation)
                throw new InvalidOperationException("The animation cannot be controlled in its current state.");
            await StepAnimation(direction).WaitAsync(token);
            return GetAutomationPlayback();
        }
    }
}
