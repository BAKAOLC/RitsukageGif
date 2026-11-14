using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotCaptureSourceContext : IDisposable
    {
        private readonly Func<DisplayTopology> _displayProvider;
        private readonly DesktopDisplayState? _displays;
        private readonly WindowPreset? _preset;
        private readonly WindowSourceLoss _sourceLoss;

        private ScreenshotCaptureSourceContext(SelectionResult selection, RecordingOptions options,
            Func<DisplayTopology> displayProvider, DesktopDisplayState? displays, WindowSourceLoss sourceLoss,
            WindowPreset? preset, NativeCaptureSessionReference? portalSource = null)
        {
            Selection = selection;
            Options = options;
            _displayProvider = displayProvider;
            _displays = displays;
            _sourceLoss = sourceLoss;
            _preset = preset;
            _portalSourceReference = portalSource;
        }

        private SelectionResult Selection { get; }
        internal RecordingOptions Options { get; }
        private IFrameSource? RawSource { get; set; }

        public void Dispose()
        {
            _displays?.Dispose();
            _portalSourceReference?.Dispose();
        }

        internal static Task<ScreenshotCaptureSourceContext> CreateAsync(SelectionResult selection,
            RecordingOptions options, Screens screens, WindowSourceLoss sourceLoss,
            CancellationToken token, double outputScale = 1)
        {
            var preset = selection.CreatePreset();
            var region = preset?.Layout is { } layout
                ? new(0, 0, layout.Width, layout.Height)
                : selection.Region;
            options = options with
            {
                Region = region, Transparent = options.Transparent || preset is not null,
                PixelDensity =
                preset?.Layout is null ? NativeDisplayCatalog.Density(selection.Region) * outputScale : 1,
            };
            options.Validate();
            if (DesktopCapture.UsesSourceSelection)
            {
                token.ThrowIfCancellationRequested();
                var source = (selection.CaptureSession ?? throw new CaptureSourceLostException()).Retain();
                try
                {
                    var topology = new DisplayTopology(
                        [source.Session.NativeBounds ?? throw new CaptureSourceLostException()]);
                    return Task.FromResult(new ScreenshotCaptureSourceContext(selection, options, () => topology, null,
                        sourceLoss, preset, source));
                }
                catch
                {
                    source.Dispose();
                    throw;
                }
            }

            var displays = new DesktopDisplayState(screens);
            try
            {
                return Task.FromResult(new ScreenshotCaptureSourceContext(selection, options, displays.Read, displays,
                    sourceLoss, preset));
            }
            catch
            {
                displays.Dispose();
                throw;
            }
        }

        internal IFrameSource CreateSource(RecordingOptions options)
        {
            RawSource = _preset is not null
                ? new SceneCapture(options, _displayProvider, _preset, nativeSource: _portalSourceReference?.Session,
                    includedWindowTitle: Selection.IncludedCaptureWindowTitle)
                {
                    TrackInputGeometry = true, WindowSourceLost = _sourceLoss.Report,
                }
                : new FollowingCapture(options, _displayProvider, () => Selection.Region,
                    _portalSourceReference?.Session, Selection.IncludedCaptureWindowTitle)
                {
                    SourceLost = _portalSourceReference?.Session.IsWindowSource == true
                        ? _sourceLoss.Report
                        : null,
                };
            return RawSource;
        }
    }
}
