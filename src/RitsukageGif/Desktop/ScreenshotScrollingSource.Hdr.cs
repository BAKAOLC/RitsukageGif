namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotScrollingSource
    {
        public CapturedFrameSet CaptureFrames(PixelRect region, CancellationToken token)
        {
            CapturedFrameSet frames;
            if (_desktop is not null)
            {
                if (_current != region)
                {
                    _desktop.Move(region);
                    _current = region;
                }

                frames = _desktop.CaptureFrames(token);
            }
            else
            {
                var target = RefreshTarget(_target!) ?? throw new CaptureSourceLostException();
                var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
                if (region.Intersect(target.Client) != region) throw new CaptureSourceLostException();
                _window ??= new(target.Handle, false, 10, authorization: target.NativeAuthorization,
                    nativeSource: _nativeSource);
                _window.ConfigureRegion(region, Math.Max(1, (int)Math.Ceiling(region.Width * _density)),
                    Math.Max(1, (int)Math.Ceiling(region.Height * _density)));
                frames = _window.CaptureFrames(token);
                var refreshed = RefreshTarget(target) ?? throw new CaptureSourceLostException();
                if (target.Client != refreshed.Client ||
                    bounds != (OperatingSystem.IsLinux() ? refreshed.Client : refreshed.Bounds))
                    throw new CaptureSourceLostException();
                if (frames.Hdr is null) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
                frames = NativeFrameComposition.Compose([(_window.CoordinateBounds ?? bounds, frames)], region,
                    _density, token);
            }

            if (frames.Hdr is not { } hdr) throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable"));
            if (_toneMap == HdrToneMapMode.HighlightCompression) return frames;
            var mapped = HdrToneMapping.Map(hdr, _toneMap, token);
            return frames with { Sdr = mapped };
        }
    }
}
