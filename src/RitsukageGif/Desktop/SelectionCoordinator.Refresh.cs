namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        internal Func<SelectionResult, CancellationToken, Task<(byte[] Pixels, CapturedFrame? Hdr)>>? RefreshCapture
        {
            get;
            // ReSharper disable once UnusedAutoPropertyAccessor.Global
            set;
        }

        private void RefreshScreenshot()
        {
            if (!ScreenshotMode || UsesLocalSource || _screenshotCompleting || _state.Dragging ||
                Annotations is not { IsBusy: false, IsDrawing: false }) return;
            _historyWork = _screenshotTask = RefreshFrozenFramesAsync();
        }

        private async Task<(byte[] Pixels, CapturedFrame? Hdr)> CaptureRefreshFrameAsync(SelectionResult selection,
            CancellationToken token)
        {
            if (RefreshCapture is { } provider) return await provider(selection, token);
            return await Task.Run(() =>
            {
                if (selection.WindowSource) return CaptureWindowPixels(selection, token);
                using var capture = new DesktopCapture(new(selection.Region, Divisor: 1, Cursor: selection.Cursor));
                var retainColor = _retainHdr || (OperatingSystem.IsWindows() &&
                                                 WindowsDisplayColor
                                                     .Read(WindowsDisplayColor.ForRegion(selection.Region)).Hdr);
                CapturedFrameSet? frames = null;
                if (retainColor)
                    try
                    {
                        frames = capture.CaptureFrames(token);
                    }
                    catch (Exception error) when (IsOptionalHdrUnsupported(error))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                frames ??= CreateSdrFrames(selection.Region.Width, selection.Region.Height, capture.Capture());
                return (PackScreenshotFrame(frames, token), frames.Hdr);
            }, token);
        }

        private async Task RefreshFrozenFramesAsync(
            Func<Action, Action, Action<Exception>, CancellationToken, Task<IDisposable>>? observe = null)
        {
            var annotations = Annotations!;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            _screenshotWork = work;
            _screenshotCompleting = true;
            IDisposable? inputObserver = null;
            FrozenScreenshotCursor? refreshedCursor = null;
            var previousCursor = ScreenshotCursor;
            try
            {
                Task<bool>? released = null;
                if (observe is not null)
                {
                    var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _frameRefreshRelease = completion;
                    released = completion.Task;
                    inputObserver = await observe(
                        () => completion.TrySetResult(true), Cancel,
                        error => completion.TrySetException(error), work.Token);
                }

                work.Token.ThrowIfCancellationRequested();
                _showing = true;
                ReleaseCaptures();
                foreach (var window in _windows) await CaptureWindowVisibility.HideAsync(window, work.Token);
                if (released is not null) await released.WaitAsync(work.Token);
                work.Token.ThrowIfCancellationRequested();
                if (RefreshCapture is null && OperatingSystem.IsWindows())
                    refreshedCursor = FrozenScreenshotCursor.Capture();
                if (WindowSource)
                {
                    var target = Snapshot.Target ?? throw new CaptureSourceLostException();
                    var frame = await CaptureRefreshFrameAsync(new(target.Client, true, target,
                        !OperatingSystem.IsWindows() && _cursor), work.Token);
                    work.Token.ThrowIfCancellationRequested();
                    var replacement = new FrozenWindowScreenshot(target, target.Client, frame.Pixels, frame.Hdr);
                    try
                    {
                        await annotations.SetOverlayFrameAsync(target.Client, frame.Pixels, work.Token);
                    }
                    catch
                    {
                        replacement.Dispose();
                        throw;
                    }

                    if (refreshedCursor is not null) ScreenshotCursor = refreshedCursor;
                    _frozenWindow?.Dispose();
                    _frozenWindow = replacement;
                }
                else
                {
                    var frames = new List<(PixelRect Region, byte[] Pixels)>();
                    var hdr = new List<(PixelRect Region, CapturedFrame? Frame)>();
                    foreach (var window in _windows)
                    {
                        var frame = await CaptureRefreshFrameAsync(new(window.Region, false, null,
                            !OperatingSystem.IsWindows() && _cursor), work.Token);
                        if (frame.Pixels.Length != checked(window.Region.Width * window.Region.Height * 4))
                            throw new ArgumentException(UiText.Get("InvalidFrameBuffer"));
                        frames.Add((window.Region, frame.Pixels));
                        hdr.Add((window.Region, frame.Hdr));
                    }

                    var previous = _screenshots.ToArray();
                    var previousHdr = _hdrScreenshots.ToArray();
                    await annotations.ReplaceFramesAsync(frames, work.Token);
                    try
                    {
                        if (refreshedCursor is not null) ScreenshotCursor = refreshedCursor;
                        _screenshots.Clear();
                        _screenshots.AddRange(frames);
                        _hdrScreenshots.Clear();
                        _hdrScreenshots.AddRange(hdr);
                        for (var index = 0; index < _windows.Count; index++)
                            _windows[index].ReplacePixels(frames[index].Pixels);
                    }
                    catch
                    {
                        ScreenshotCursor = previousCursor;
                        _screenshots.Clear();
                        _screenshots.AddRange(previous);
                        _hdrScreenshots.Clear();
                        _hdrScreenshots.AddRange(previousHdr);
                        await annotations.ReplaceFramesAsync(previous, CancellationToken.None);
                        for (var index = 0; index < _windows.Count; index++)
                            _windows[index].ReplacePixels(previous[index].Pixels);
                        throw;
                    }
                }

                if (refreshedCursor is not null)
                {
                    previousCursor?.Dispose();
                    refreshedCursor = null;
                }
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (!_finished) annotations.SetExportState(false, error.Message);
                HistoryError?.Invoke(error);
            }
            finally
            {
                inputObserver?.Dispose();
                _frameRefreshRelease = null;
                refreshedCursor?.Dispose();
                _screenshotCompleting = false;
                if (ReferenceEquals(_screenshotWork, work)) _screenshotWork = null;
                if (!_finished)
                {
                    foreach (var window in _windows) window.Show();
                    _windows.FirstOrDefault(window => window.Region.Contains(_pointer))?.Activate();
                    Refresh();
                }

                _showing = false;
            }
        }
    }
}
