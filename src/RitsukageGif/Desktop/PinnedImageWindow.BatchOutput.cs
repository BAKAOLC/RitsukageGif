namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Func<OutputColorMode> OutputColorModeProvider { get; set; } = () => OutputColorMode.Sdr;
        internal bool CanSaveSelection => !_closed && _annotation is null;
        internal CancellationToken OutputLifetime => _lifetime.Token;

        internal Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return ScreenshotSaveCompletion.RunAsync(paths, profile, Clipboard, StorageProvider,
                () => CopyImageAsync(output),
                OutputNotification, CancellationToken.None);
        }

        internal Task<ScreenshotResult> CaptureOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_annotation is not null) return ComposeOutputAsync(mode, token);
            var png = _hdrSource is null ? _png : _sdrPng ?? _png;
            var hdr = _hdrSource;
            var captureScaling = OriginalCaptureScaling;
            var region = _isThumbnail && CurrentPreferences.CopyVisibleRegion ? DisplayRegion : (PixelRect?)null;
            return CaptureAsync();

            async Task<ScreenshotResult> CaptureAsync()
            {
                await CropGate.WaitAsync(token);
                try
                {
                    return await Task.Run(() =>
                    {
                        if (region is { } bounds)
                        {
                            png = PngImageCodec.Crop(png, bounds, token);
                            if (hdr is not null) hdr = CropOutputFrame(hdr, bounds, token);
                        }

                        return CreateOutput(png, hdr, mode, captureScaling);
                    }, token);
                }
                finally
                {
                    CropGate.Release();
                }
            }
        }

        private async Task<ScreenshotResult> ComposeOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            var session = _annotation ?? throw new InvalidOperationException();
            var source = _hdrSource is null ? _png : _sdrPng ?? _png;
            var hdr = _hdrSource;
            var captureScaling = OriginalCaptureScaling;
            session.SetExportState(true);
            try
            {
                var png = await session.ComposeAsync(session.Selection, source, token);
                if (hdr is not null && session.HasAnnotations)
                    hdr = await session.ComposeHdrAsync(session.Selection, hdr, token: token);
                return CreateOutput(png, hdr, mode, captureScaling);
            }
            finally
            {
                if (ReferenceEquals(session, _annotation)) session.SetExportState(false);
            }
        }

        private static ScreenshotResult CreateOutput(byte[] png, CapturedFrame? hdr, OutputColorMode mode,
            double captureScaling)
        {
            mode = mode switch
            {
                OutputColorMode.Sdr => OutputColorMode.Sdr,
                OutputColorMode.Hdr => hdr is { ColorInfo.IsHdrSource: true }
                    ? OutputColorMode.Hdr
                    : throw new NotSupportedException(UiText.Get("HdrCaptureUnavailable")),
                OutputColorMode.Both => hdr is { ColorInfo.IsHdrSource: true }
                    ? OutputColorMode.Both
                    : OutputColorMode.Sdr,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            return new(png, ScreenshotOutputAction.Save, hdr) { ColorMode = mode, CaptureScaling = captureScaling };
        }

        private static CapturedFrame CropOutputFrame(CapturedFrame frame, PixelRect region, CancellationToken token)
        {
            var pixels = new byte[checked(region.Width * region.Height * 8)];
            for (var y = 0; y < region.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                frame.Pixels.Span.Slice((region.Y + y) * frame.Stride + region.X * 8, region.Width * 8)
                    .CopyTo(pixels.AsSpan(y * region.Width * 8));
            }

            var colors = new List<FrameColorRegion>();
            foreach (var color in frame.ColorRegions)
            {
                var intersection = color.Region.Intersect(region);
                if (!intersection.IsEmpty)
                    colors.Add(color with
                    {
                        Region = intersection with { X = intersection.X - region.X, Y = intersection.Y - region.Y },
                    });
            }

            return frame with
            {
                Pixels = pixels, Width = region.Width, Height = region.Height, Stride = checked(region.Width * 8),
                ColorRegions = colors.AsReadOnly(),
            };
        }
    }
}
