namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        public Func<OutputColorMode> OutputColorModeProvider { get; set; } = () => OutputColorMode.Sdr;
        private bool CanSaveSelection => !_closed && _annotation is null && LiveSourceReady;
        private CancellationToken OutputLifetime => _lifetime.Token;

        private Task CompleteSelectionSaveAsync(IReadOnlyList<string> paths, ScreenshotSaveProfile profile,
            ScreenshotResult output)
        {
            return ScreenshotSaveCompletion.RunAsync(paths, profile, Clipboard, StorageProvider,
                () => CopyImageAsync(output),
                OutputNotification, CancellationToken.None);
        }

        private Task<ScreenshotResult> CaptureOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            var snapshot = SnapshotOutputView();
            return PinnedViewportOutput.ProjectAsync(CaptureSceneOutputAsync(mode, token), snapshot, token);
        }

        private async Task<ScreenshotResult> CaptureSceneOutputAsync(OutputColorMode mode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_closed, this);
            await WaitForRasterUpdatesAsync(token);
            var session = _annotationScene;
            session?.SetExportState(true);
            try
            {
                var pending = CaptureLiveOutputAsync(mode, token) ?? CaptureAnimationFrameAsync(mode, token);
                var source = pending is null
                    ? CreateOutput(EffectivePng, EffectiveHdr, mode, OriginalCaptureScaling)
                    : await pending;
                var (png, hdr) = await ComposeTextMarkupAsync(source.Png, source.Hdr, mode, token);
                if (session is not null)
                {
                    png = await session.ComposeAsync(session.Selection, png, token);
                    if (mode != OutputColorMode.Sdr && hdr is not null && session.HasAnnotations)
                        hdr = await session.ComposeHdrAsync(session.Selection, hdr, token: token);
                }

                return source with { Png = png, Hdr = hdr, WindowShape = Presentation.WindowShape };
            }
            finally
            {
                if (session is not null && ReferenceEquals(session, _annotationScene)) session.SetExportState(false);
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
    }
}
