using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal Func<HdrToneMapMode> CaptureToneMapProvider { get; set; } = () => HdrToneMapMode.HighlightCompression;

        internal async Task PinWindowRoiAsync(WindowTarget target, PixelRect region, bool cursor,
            double captureScaling = 1, CancellationToken token = default)
        {
            var descriptor = await Task.Run(() => WindowRoiPinSource.Describe(target, region, cursor), token)
                .ConfigureAwait(false);
            var bounds = OperatingSystem.IsLinux() ? target.Client : target.Bounds;
            var position = new PixelPoint(bounds.X + descriptor.RelativeRegion.X,
                bounds.Y + descriptor.RelativeRegion.Y);
            var duplicate = await Dispatcher.UIThread.InvokeAsync(() => ResolveLiveDuplicateAsync(descriptor, token),
                DispatcherPriority.Normal, token);
            if (await duplicate.ConfigureAwait(false)) return;
            var source = new WindowRoiPinSource(descriptor, CaptureToneMapProvider);
            var transferred = false;
            try
            {
                // ReSharper disable once AccessToDisposedClosure
                var captured = await Task.Run(() => source.Capture(token), token).ConfigureAwait(false);
                using var frame = captured.Frame ?? throw new CaptureSourceLostException();
                var content = await frame.PrepareContentAsync(token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var window = PinnedImageWindow.CreatePrepared(content, () => CanRestoreHidden(), Preferences);
                    window.SetOriginalCaptureScaling(captureScaling);
                    window.InitializeLiveCapture(descriptor, source, frame);
                    AttachWindow(window);
                    try
                    {
                        window.SetCapturePlacement(position, default);
                        ShowNewPin(window);
                        transferred = true;
                    }
                    catch
                    {
                        _windows.Remove(window);
                        window.Dispose();
                        throw;
                    }

                    ScheduleSave();
                    Changed?.Invoke(this, EventArgs.Empty);
                }, DispatcherPriority.Normal, token);
            }
            finally
            {
                if (!transferred) source.Dispose();
            }
        }

        private async Task<bool> ResolveLiveDuplicateAsync(WindowRoiPinDescriptor descriptor, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var behavior = Preferences.DuplicateBehavior;
            if (behavior == PinnedDuplicateBehavior.RestoreLastClosed &&
                await TryRestoreMatchingLiveAsync(descriptor, token)) return true;
            if (behavior == PinnedDuplicateBehavior.CreateNew ||
                _windows.FirstOrDefault(window =>
                        window.Group == Preferences.ActiveGroup && window.MatchesLiveSource(descriptor)) is not
                    { } existing) return false;
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (behavior)
            {
                case PinnedDuplicateBehavior.FocusExisting:
                    RevealWindow(existing);
                    existing.Activate();
                    break;
                case PinnedDuplicateBehavior.RestoreLastClosed:
                    RevealWindow(existing);
                    break;
                case PinnedDuplicateBehavior.Ignore: break;
                default: throw new InvalidOperationException(nameof(Preferences));
            }

            return true;
        }
    }
}
