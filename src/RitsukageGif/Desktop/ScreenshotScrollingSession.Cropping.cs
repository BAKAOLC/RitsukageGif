namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSession
    {
        private bool _viewportMatched;

        internal int OutputAxisLength
        {
            get
            {
                lock (_sync) return _axis == ScrollCaptureAxis.Vertical ? _stitching.Height : _stitching.Width;
            }
        }

        internal PixelPoint? OutputOrigin
        {
            get
            {
                lock (_sync)
                {
                    if (_firstRegion is not { } first) return null;
                    var offset = checked((int)Math.Round((_leadingInset + _stitching.StartOffset) / _density));
                    return _axis == ScrollCaptureAxis.Vertical
                        ? new(first.X, checked(first.Y + offset))
                        : new(checked(first.X + offset), first.Y);
                }
            }
        }

        internal PixelRect PreviewViewport
        {
            get
            {
                lock (_sync)
                    return _axis == ScrollCaptureAxis.Vertical
                        ? new(0, _stitching.CurrentOffset - _stitching.StartOffset,
                            PhysicalSize(_initialRegion.Width),
                            PhysicalSize(_initialRegion.Height) - _leadingInset - _trailingInset)
                        : new(_stitching.CurrentOffset - _stitching.StartOffset, 0,
                            PhysicalSize(_initialRegion.Width) - _leadingInset - _trailingInset,
                            PhysicalSize(_initialRegion.Height));
            }
        }

        internal bool HasMatchedViewport
        {
            get
            {
                lock (_sync) return _viewportMatched;
            }
        }

        internal bool CanCropLeading
        {
            get
            {
                lock (_sync) return _viewportMatched && _stitching.CanCropBeforeCurrentViewport;
            }
        }

        internal bool CanCropTrailing
        {
            get
            {
                lock (_sync) return _viewportMatched && _stitching.CanCropAfterCurrentViewport;
            }
        }

        internal bool CropLeading(CancellationToken token = default)
        {
            return CropToViewport(true, token);
        }

        internal bool CropTrailing(CancellationToken token = default)
        {
            return CropToViewport(false, token);
        }

        private bool CropToViewport(bool leading, CancellationToken token)
        {
            lock (_sync)
            {
                token.ThrowIfCancellationRequested();
                if (!_viewportMatched) return false;
                var changed = leading
                    ? _stitching.CropBeforeCurrentViewport(token)
                    : _stitching.CropAfterCurrentViewport(token);
                if (!changed) return false;
                _captureEpoch++;
                Interlocked.Increment(ref _generation);
                var wasPaused = Paused;
                Paused = true;
                try
                {
                    PublishCurrentImage();
                }
                finally
                {
                    Paused = wasPaused;
                }

                Logger.ZLogInformation(
                    $"Scroll capture viewport crop applied. SessionId={_logSessionId} Leading={leading} StartOffset={_stitching.StartOffset} CurrentOffset={_stitching.CurrentOffset} Width={_stitching.Width} Height={_stitching.Height} Generation={_generation}");
                return true;
            }
        }

        private void PublishCurrentImage()
        {
            Publish(new(ScrollCaptureStatus.AlreadyCovered, _stitching.CurrentOffset, 0, 1,
                _stitching.Width, _stitching.Height), true);
        }
    }
}
