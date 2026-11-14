using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using Avalonia_PixelPoint = Avalonia.PixelPoint;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator : IDisposable
    {
        private static readonly ILogger SelectionLogger = LogHub.GetLogger("Desktop.Selection");
        private static long _nextSelectionId;
        private readonly CancellationTokenSource _cancel = new();

        private readonly TaskCompletionSource<SelectionResult?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly int _fps;
        private readonly int _radius, _threshold;

        private readonly Screens _screens;
        private readonly List<(PixelRect Region, byte[] Pixels)> _screenshots = [];
        private readonly long _selectionId = Interlocked.Increment(ref _nextSelectionId);
        private readonly SelectionState _state = new();
        private readonly List<OverlayWindow> _windows = [];
        private Key? _confirmationKey, _cancellationKey;
        private PixelRect _controlBounds;
        private bool _controlPending;
        private PixelPoint? _controlPoint;
        private nint _controlWindow;
        private bool _cursor;
        private PixelRect _hovered;
        private WindowTarget? _hoveredTarget, _selectedTarget;
        private SelectionSnapshot? _lastSnapshot;
        private bool _left, _right, _escape, _enter, _shift, _finished, _started, _showing, _focusCheckPending;
        private PixelPoint _mouse, _pointer;
        private bool _pendingFinish, _releasingCapture, _selectionIsWholeTarget;
        private SelectionResult? _pendingResult;
        private bool _portalSeparateCursor;
        private PixelRect _pressRegion;
        private bool _rightConfirm;
        private long _shiftDown;
        private IDisposable? _shiftTimer;
        private NativeCaptureSelection? _sourceSelection;
        private IReadOnlyList<WindowTarget> _targets = [];

        public SelectionCoordinator(Screens screens, int radius, int threshold, bool windowSource = false,
            bool cursor = false, int fps = 20, bool retainScreenshot = false, bool retainHdr = false,
            HdrToneMapMode screenshotToneMap = HdrToneMapMode.HighlightCompression,
            OutputColorMode screenshotColorMode = OutputColorMode.Sdr)
        {
            if (!Enum.IsDefined(screenshotToneMap)) throw new ArgumentOutOfRangeException(nameof(screenshotToneMap));
            if (!Enum.IsDefined(screenshotColorMode))
                throw new ArgumentOutOfRangeException(nameof(screenshotColorMode));
            _screenshotToneMap = screenshotToneMap;
            _screens = screens;
            NativeDisplayCatalog.Update(screens);
            WindowSource = windowSource;
            _cursor = cursor;
            _fps = fps;
            _radius = radius;
            _threshold = threshold;
            ScreenshotMode = retainScreenshot;
            if (!ScreenshotMode) ShortcutBindings = [.. ShortcutBindings.Where(binding => !binding.ScreenshotOnly)];
            _retainHdr = retainScreenshot && retainHdr;
            InitializeAnnotations();
            if (Annotations is { } annotations) annotations.OutputColorMode = screenshotColorMode;
        }

        internal bool ScreenshotMode { get; }

        private int Mode =>
            _shift && !_shiftUsedAsModifier && Stopwatch.GetElapsedTime(_shiftDown).TotalMilliseconds >= 200
                ? 0
                : SnappingMode;

        internal bool Controls { get; private set; }

        internal bool WindowSource { get; private set; }

        internal bool CanSwitchSource => !UsesLocalSource;

        internal bool CanDetectControls => CanSwitchSource && (OperatingSystem.IsWindows() ||
                                                               OperatingSystem.IsMacOS() ||
                                                               LinuxAccessibilityLocator.Available);

        internal SelectionSnapshot Snapshot
        {
            get
            {
                EnsureShapePreview();
                var committed = !_state.Selected.IsEmpty || _state.Dragging || ShapeDrawing;
                var target = committed ? _selectedTarget : _hoveredTarget;
                var display = committed ? _state.Selected : _hovered;
                display = _state switch
                {
                    { Dragging: true, DragThresholdReached: false } => _pressRegion,
                    _ => display,
                };
                if (!_state.Dragging && _selectionIsWholeTarget && target is not null)
                    display = WindowSource ? target.Client : target.Bounds;
                if (WindowSource && !UsesLocalSource && ShapeMode != SelectionShapeMode.MultiWindow)
                    display = target is not null ? display.Intersect(target.Client) : default;
                var phase = _pendingFinish || _screenshotCompleting
                    ? SelectionPhase.Confirming
                    : ShapeDrawing
                        ? SelectionPhase.Dragging
                        : ManipulationPhase ?? (_state.Dragging
                            ? _state.DragThresholdReached ? SelectionPhase.Dragging : SelectionPhase.Pressed
                            : committed
                                ? SelectionPhase.Selected
                                : SelectionPhase.Candidate);
                return new(phase, display, target, WindowSource, Controls,
                    WindowSource && target is not null && !UsesLocalSource,
                    !display.IsEmpty && !_screenshotCompleting && !ShapeDrawing,
                    _cursor, _selectionShape, DisplayShapeMode);
            }
        }

        public void Dispose()
        {
            Cancel();
            DisposeWindowsKeyGuard();
            DisposeFrameDecorationPreview();
            _sourceSelection?.Dispose();
            _sourceSelection = null;
            _screenshots.Clear();
            _nativeScreenshots.Clear();
            _hdrScreenshots.Clear();
            _frozenWindow?.Dispose();
            _frozenWindow = null;
            ScreenshotCursor?.Dispose();
            ScreenshotCursor = null;
            _shapeModeTimer?.Dispose();
            _polygonCornerPaint?.Dispose();
            _polygonCornerPaint = null;
            Annotations?.Dispose();
        }

        internal event Action<SelectionSnapshot>? SelectionChanged;

        public async Task<SelectionResult?> ShowAsync()
        {
            SelectionLogger.ZLogInformation(
                $"Selection initialization started. SelectionId={_selectionId} Screenshot={ScreenshotMode} WindowSource={WindowSource} MonitorCount={_screens.All.Count}");
            Dispatcher.UIThread.VerifyAccess();
            if (_started) throw new InvalidOperationException();
            _started = true;
            using var shortcuts = CreateSelectionShortcutScope();
            _screens.Changed += ScreensChanged;
            try
            {
                if (!_finished) await InitializeAsync();
                return await _completion.Task;
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            {
                Finish(null);
                return await _completion.Task;
            }
            catch (Exception error)
            {
                SelectionLogger.ZLogError(error, $"Selection failed. SelectionId={_selectionId}");
                Finish(null, error);
                return await _completion.Task;
            }
            finally
            {
                Finish(null);
                try
                {
                    await Task.WhenAll(_screenshotTask ?? Task.CompletedTask,
                        _scrollingTask ?? Task.CompletedTask, _codeDetection?.Pending ?? Task.CompletedTask,
                        _candidateRefreshTask ?? Task.CompletedTask);
                }
                finally
                {
                    await WaitForWindowsKeyGuardsAsync();
                    _screens.Changed -= ScreensChanged;
                    _cancel.Dispose();
                }
            }
        }

        private async Task InitializeAsync()
        {
            if (ScreenshotMode && !DesktopCapture.UsesSourceSelection)
                ScreenshotCursor = FrozenScreenshotCursor.Capture();
            if (DesktopCapture.UsesSourceSelection)
            {
                _sourceSelection =
                    await DesktopCapture.BeginSourceSelectionAsync(WindowSource, !ScreenshotMode && _cursor, _fps,
                        _retainHdr, _cancel.Token);
                var source = _sourceSelection.Frame;
                if (_finished) return;
                var region = source.Bounds;
                var (sourceFrames, _, cursor, cursorPixels, capabilities, _) =
                    _sourceSelection.ReadSnapshot(_cancel.Token);
                _portalSeparateCursor = capabilities?.SeparateCursor == true;
                var logicalFrames = NativeFrameComposition.Compose([(region, sourceFrames)], region, 1, _cancel.Token);
                var sourcePixels = PackScreenshotFrame(logicalFrames, _cancel.Token);
                if (ScreenshotMode)
                {
                    _screenshots.Add((region, sourcePixels));
                    _nativeScreenshots.Add((region, sourceFrames));
                    _hdrScreenshots.Add((region, sourceFrames.Hdr));
                    ScreenshotCursor =
                        FrozenScreenshotCursor.FromNative(cursor, cursorPixels);
                }

                _hovered = region;
                var previewScreen = _screens.Primary;
                var working = previewScreen?.WorkingArea;
                var displayScale = previewScreen?.Scaling ?? 1;
                var scale = Math.Max(1, Math.Max(region.Width / ((working?.Width ?? 1280) / displayScale * .9),
                    region.Height / ((working?.Height ?? 800) / displayScale * .85)));
                _windows.Add(new(this, region, scale, sourcePixels, _radius, _threshold, true));
            }
            else
            {
                if (OperatingSystem.IsMacOS()) await MacWindowSystem.EnsurePermissionAsync(_cancel.Token);
                if (_finished) return;
                if (OperatingSystem.IsWindows()) _windowMonitor = new(CandidatesChanged);
                _targets = await Task.Run(WindowLocator.ListCandidates, _cancel.Token);
                _candidatesInitialized = true;
                if (_candidatesDirty) CandidatesChanged();
                if (OperatingSystem.IsMacOS())
                {
                    var controlsAvailable =
                        await Task.Run(() => MacWindowSystem.CheckControlPermission(), _cancel.Token);
                    if (!controlsAvailable) Controls = false;
                }
                else if (OperatingSystem.IsLinux())
                {
                    await LinuxAccessibilityLocator.EnsureAvailableAsync(_cancel.Token);
                    Controls = DetectionMode == CaptureDetectionMode.Controls && LinuxAccessibilityLocator.Available;
                }

                if (_finished) return;
                foreach (var screen in _screens.All.ToArray())
                {
                    var b = screen.Bounds;
                    var region = new PixelRect(b.X, b.Y, b.Width, b.Height);
                    SelectionLogger.ZLogDebug(
                        $"Selection monitor capture started. SelectionId={_selectionId} Region={region} Scaling={screen.Scaling}");
                    var retainColor = _retainHdr || (OperatingSystem.IsWindows() &&
                                                     WindowsDisplayColor.Read(WindowsDisplayColor.ForRegion(region))
                                                         .Hdr);
                    var frames = await Task.Run(() =>
                    {
                        var captureOptions = new RecordingOptions(region, Divisor: 1, Cursor: false)
                            { PixelDensity = ScreenshotMode ? NativeDisplayCatalog.Density(region) : 1 };
                        using var capture = new DesktopCapture(captureOptions);
                        if (retainColor)
                            try
                            {
                                return capture.CaptureFrames(_cancel.Token);
                            }
                            catch (Exception error) when (IsOptionalHdrUnsupported(error))
                            {
                                _cancel.Token.ThrowIfCancellationRequested();
                            }

                        return CreateSdrFrames(captureOptions.Width, captureOptions.Height, capture.Capture());
                    }, _cancel.Token);
                    if (_finished) return;
                    var logicalFrames = frames.Sdr.Width == region.Width && frames.Sdr.Height == region.Height
                        ? frames
                        : NativeFrameComposition.Compose([(region, frames)], region, 1, _cancel.Token);
                    var pixels = PackScreenshotFrame(logicalFrames, _cancel.Token);
                    if (ScreenshotMode && OperatingSystem.IsMacOS()) _nativeScreenshots.Add((region, frames));
                    if (retainColor) _hdrScreenshots.Add((region, frames.Hdr));
                    if (ScreenshotMode) _screenshots.Add((region, pixels));
                    _windows.Add(new(this, region, screen.Scaling, pixels, _radius, _threshold));
                    SelectionLogger.ZLogDebug(
                        $"Selection monitor capture completed. SelectionId={_selectionId} Region={region} RetainedColor={retainColor}");
                }
            }

            if (_finished) return;
            if (_windows.Count == 0)
            {
                Finish(null);
                return;
            }

            _showing = true;
            ApplyInitialRegion();
            try
            {
                foreach (var window in _windows.ToArray())
                {
                    if (_finished) return;
                    window.Show();
                    WindowDiagnostics.Write(SelectionLogger, "SelectionOverlayShown", window);
                }

                if (_finished) return;
                OverlayWindow? active = null;
                if (OperatingSystem.IsWindows())
                {
                    _pointer = NativeMethods.Mouse();
                    active = _windows.FirstOrDefault(window => window.Region.Contains(_pointer));
                    UpdatePoint();
                    Refresh();
                }

                await StartWindowsKeyGuardAsync();
                if (_finished) return;
                (active ?? _windows[0]).Activate();
                PrepareWindowScreenshot();
                SelectionLogger.ZLogInformation(
                    $"Selection overlays ready. SelectionId={_selectionId} OverlayCount={_windows.Count}");
                QueueAutoConfirm();
            }
            finally
            {
                _showing = false;
                FocusChanged();
            }
        }

        private void ScreensChanged(object? sender, EventArgs args)
        {
            Cancel();
        }

        internal void FocusChanged()
        {
            Annotations?.ReleasePolylineArrowKey();
            if (_selectionDialogDepth > 0) return;
            if (!_showing) StopFrameRefresh();
            CancelModifierTap();
            AuxiliaryVisible = CapturePreferences.AuxiliaryPolicy == CaptureAuxiliaryPolicy.Always;
            if (!CapturePreferences.QuitOnForeignActivation || _scrolling ||
                _finished || _showing || _screenshotCompleting || _focusCheckPending) return;
            _focusCheckPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _focusCheckPending = false;
                if (!_finished && !_showing && !_scrolling && !_screenshotCompleting && _selectionDialogDepth == 0 &&
                    !_windows.Any(window => window.IsActive) && IsForeignCaptureWindowActivated())
                {
                    SelectionLogger.ZLogInformation(
                        $"Selection cancelled after losing application focus. SelectionId={_selectionId} Scrolling={_scrolling} OverlayCount={_windows.Count}");
                    Finish(null);
                }
            }, DispatcherPriority.Background);
        }

        internal void CaptureLost()
        {
            if (_finished || _releasingCapture || (_showing && FrameRefreshHeld)) return;
            StopFrameRefresh();
            _mouseInputHeld = false;
            _left = _right = false;
            _rightConfirm = false;
            if (CancelSelectionManipulation())
            {
                Refresh();
                return;
            }

            if (_pendingFinish)
            {
                Finish(null);
                return;
            }

            if (!_state.Dragging) return;
            ClearSelection();
            Refresh();
        }

        internal void PointerInput(PixelPoint point, bool left, bool right, bool shift, bool pressed = false,
            int clickCount = 1)
        {
            if (_finished || _screenshotCompleting || ScreenshotDrawing || Annotations is { IsBusy: true }) return;
            try
            {
                if (_pendingFinish)
                {
                    _left = left;
                    _right = right;
                    if (AllInputReleased) CompleteSelection(_pendingResult);
                    return;
                }

                _pointer = point;
                UpdateShift(shift);
                UpdatePoint();
                if (HandleShapePointer(point, left, right, pressed, clickCount)) return;
                if (pressed && left && clickCount >= 2)
                {
                    _left = left;
                    _right = right;
                    Confirm();
                    Refresh();
                    return;
                }

                if (HandleSelectionManipulation(point, left, right, pressed))
                {
                    _left = left;
                    _right = right;
                    if (!left && !right) ReleaseCaptures();
                    Refresh();
                    return;
                }

                if (pressed && left && !_left && !IgnoreOutsideScreenshotPress(point))
                {
                    _selectionShape = null;
                    PrepareOutsideExpansion();
                    UpdatePoint(true);
                    _selectedTarget = _hoveredTarget;
                    MeasurementControlBounds = Controls ? _controlBounds : default;
                    _pressRegion = ResolveClickTarget(_selectedTarget);
                    _selectionIsWholeTarget = false;
                    var screen = _screens.All.FirstOrDefault(item =>
                        item.Bounds.Contains(new Avalonia_PixelPoint(_pointer.X, _pointer.Y)));
                    var scale = UsesLocalSource && _windows.Count > 0
                        ? _windows[0].Region.Width / Math.Max(1, _windows[0].ClientSize.Width)
                        : screen?.Scaling ?? 1;
                    _state.Begin(_mouse, _pointer, 4 * scale);
                    _geometryAnchor = _mouse;
                }

                if (_state.Dragging) MoveGeometrySelection();
                if (!left && _left && _state.Dragging) EndSelection();
                if (pressed && right && !_right) _rightConfirm = true;
                var confirm = !right && _right && _rightConfirm;
                _left = left;
                _right = right;
                if (!right) _rightConfirm = false;
                if (confirm)
                {
                    if (CancelShapeDrawing())
                    {
                        Refresh();
                        return;
                    }

                    if (_state.Selected.IsEmpty && !_state.Dragging) RequestFinish(null);
                    else ClearSelection();
                }

                if (!left && !right) ReleaseCaptures();
                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        internal void KeyInput(Key key, bool down, KeyModifiers modifiers)
        {
            if (_finished) return;
            if (HandleTextInputKey(key, down)) return;
            if (Annotations?.HandleNumberCaptionKey(key, modifiers, down) == true) return;
            try
            {
                if (!down) ReleaseSelectionKey(key);
                if (_pendingFinish)
                {
                    if (AllInputReleased) CompleteSelection(_pendingResult);
                    return;
                }

                if (HandleShapeModeKey(down, modifiers)) return;
                UpdateShift(modifiers.HasFlag(KeyModifiers.Shift));
                AuxiliaryVisible = CapturePreferences.AuxiliaryPolicy == CaptureAuxiliaryPolicy.Always ||
                                   (modifiers.HasFlag(KeyModifiers.Alt) &&
                                    CapturePreferences.AuxiliaryPolicy == CaptureAuxiliaryPolicy.Alt);
                if (SelectionCommands.TryResolve(key, modifiers, out var definition, ShortcutBindings, Annotations))
                {
                    if (definition!.Command == SelectionCommand.Confirm) _confirmationKey = key;
                    if (definition.Command == SelectionCommand.Cancel) _cancellationKey = key;
                }

                if (_screenshotCompleting && definition?.Command != SelectionCommand.Cancel)
                {
                    if (!down && key is Key.LeftShift or Key.RightShift) ReleaseTextInputKey(key);
                    return;
                }

                if (down) UpdatePoint();
                HandleSelectionKey(key, down, modifiers);
                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        private void ClearSelection(bool releaseCaptures = true)
        {
            _autoConfirmPending = false;
            CancelSelectionManipulation();
            _state.Clear();
            _selectionShape = null;
            _shapePoints.Clear();
            _shapePreviewDirty = false;
            _regionEditBase = null;
            _selectionWindows.Clear();
            _selectedTarget = null;
            MeasurementControlBounds = default;
            _selectionIsWholeTarget = false;
            _rightConfirm = false;
            _pendingFinish = false;
            _pendingResult = null;
            if (releaseCaptures) ReleaseCaptures();
            UpdatePoint();
        }

        private void UpdateShift(bool shift)
        {
            if (shift == _shift) return;
            _shiftTimer?.Dispose();
            _shiftTimer = null;
            if (shift)
            {
                _shiftUsedAsModifier = false;
                _shiftDown = Stopwatch.GetTimestamp();
                _shiftTimer = DispatcherTimer.RunOnce(() =>
                {
                    if (_finished || !_shift) return;
                    try
                    {
                        UpdatePoint();
                        if (_state.Dragging) MoveGeometrySelection();
                        Refresh();
                    }
                    catch (Exception error)
                    {
                        Finish(null, error);
                    }
                }, TimeSpan.FromMilliseconds(200));
            }

            _shift = shift;
        }

        private void ToggleControls()
        {
            if (!CanDetectControls || _state.Dragging || SelectionManipulating || _finished) return;
            if (!Controls && OperatingSystem.IsMacOS() && !MacWindowSystem.ControlDetectionAvailable)
            {
                _ = EnableMacControlsAsync();
                return;
            }

            Controls = !Controls;
            DetectionMode = Controls ? CaptureDetectionMode.Controls : CaptureDetectionMode.Windows;
            PreferencesChanged?.Invoke();
            InvalidateControlDetection();
            UpdatePoint();
            Refresh();
        }

        private void ToggleSource()
        {
            if (!CanSwitchSource || _state.Dragging || SelectionManipulating || _finished) return;
            InvalidateControlDetection();
            if (ScreenshotMode && Annotations is { } annotations)
            {
                if (annotations.IsBusy || _screenshotCompleting) return;
                _screenshotTask = SwitchScreenshotSourceAsync(!WindowSource);
                return;
            }

            WindowSource = !WindowSource;
            PreferencesChanged?.Invoke();
            UpdatePoint();
            Refresh();
            PrepareWindowScreenshot();
        }

        private void UpdatePoint(bool forceCandidate = false)
        {
            ApplyPendingInitialGeometry();
            if (!UsesLocalSource && (forceCandidate || _state is { Dragging: false, Selected.IsEmpty: true }))
            {
                _hoveredTarget = CandidateAtPoint(_pointer);
                _hovered = _hoveredTarget is { } target
                    ? WindowSource ? target.Client : target.Bounds
                    : default;
                if (CanDetectHoverControl && _hoveredTarget is { } controlTarget)
                {
                    if (TryHierarchyCandidate(controlTarget, out var candidate)) _hovered = candidate;
                    else if (_controlWindow == controlTarget.Handle && _controlPoint == _pointer &&
                             !_controlBounds.IsEmpty)
                        _hovered = _controlBounds.Intersect(_hovered);
                    else if (!_controlPending) _ = DetectControlAsync(controlTarget, _pointer);
                }

                _hovered = DetectionMode == CaptureDetectionMode.None ? default : ApplyDetectionMargin(_hovered);
                if (WindowSource && ShapeMode == SelectionShapeMode.MultiWindow && _hoveredTarget is { } windowTarget)
                    _hovered = _hovered.Intersect(windowTarget.Client);
                if (ShapeMode is SelectionShapeMode.Free or SelectionShapeMode.Polygon) _hovered = default;
                _hovered = ApplyMultiWindowCandidateWheel(_hovered);
            }

            if (AuxiliaryVisible && CapturePreferences.GuideMeasurements && Controls && !UsesLocalSource &&
                !_controlPending && _controlPoint != _pointer && CurrentControlTarget is { } measurementTarget)
                _ = DetectControlAsync(measurementTarget, _pointer);
            var current = _windows.FirstOrDefault(window => window.Region.Contains(_pointer));
            _mouse = current?.Snap(_pointer, Mode) ?? _pointer;
            _mouse = ConstrainGeometryPoint(_mouse);
        }

        private async Task DetectControlAsync(WindowTarget target, PixelPoint point)
        {
            _controlPending = true;
            var revision = _controlRevision;
            var windowSource = WindowSource;
            try
            {
                var token = _cancel.Token;
                var hierarchy = await FindControlHierarchyAsync(target, point, token);
                if (_finished || !Controls || revision != _controlRevision || windowSource != WindowSource ||
                    _pointer != point || CurrentControlTarget is not { } currentTarget ||
                    currentTarget.Handle != target.Handle || currentTarget.ProcessId != target.ProcessId ||
                    currentTarget.Bounds != target.Bounds || currentTarget.Client != target.Client) return;
                _controlWindow = target.Handle;
                _controlPoint = point;
                _controlHierarchy = hierarchy;
                UpdateCandidateHierarchy(target, point, hierarchy);
                var result = hierarchy.Count == 0 ? null : hierarchy[^1];
                _controlBounds = result is not null &&
                                 (!OperatingSystem.IsWindows() ||
                                  NativeMethods.IsWindowVisible(result.Handle == 0 ? target.Handle : result.Handle) !=
                                  0)
                    ? result.Bounds
                    : default;
                if (CanDetectHoverControl && _state is { Dragging: false, Selected.IsEmpty: true } &&
                    _pointer == point &&
                    _hoveredTarget?.Handle == target.Handle &&
                    !_controlBounds.IsEmpty)
                {
                    _hovered = ApplyDetectionMargin(TryHierarchyCandidate(target, out var candidate)
                        ? candidate
                        : _controlBounds.Intersect(WindowSource ? target.Client : target.Bounds));
                    Refresh();
                }
                else if (AuxiliaryVisible && CapturePreferences.GuideMeasurements)
                {
                    Refresh();
                }
            }
            catch (OperationCanceledException) when (_finished)
            {
            }
            catch (Exception)
            {
                // A disappearing or inaccessible control falls back to its top-level window.
                _controlBounds = default;
            }
            finally
            {
                _controlPending = false;
                if (!_finished && Controls &&
                    ((CanDetectHoverControl && _state is { Dragging: false, Selected.IsEmpty: true }) ||
                     ScreenshotDrawing ||
                     (AuxiliaryVisible && CapturePreferences.GuideMeasurements)) &&
                    CurrentControlTarget is { } current &&
                    (revision != _controlRevision || windowSource != WindowSource ||
                     _pointer != point || current.Handle != target.Handle))
                    _ = DetectControlAsync(current, _pointer);
            }
        }

        private PixelRect ResolveClickTarget(WindowTarget? target)
        {
            if (!CanDetectHoverControl || target is null) return _hovered;
            if (TryHierarchyCandidate(target, out var candidate)) return ApplyDetectionMargin(candidate);
            var basis = WindowSource ? target.Client : target.Bounds;
            if (_controlPoint == _pointer && _controlWindow == target.Handle && !_controlBounds.IsEmpty)
                return ApplyDetectionMargin(_controlBounds.Intersect(basis));
            if (!OperatingSystem.IsWindows()) return ApplyDetectionMargin(basis);
            try
            {
                // Resolve a quick click before committing it; the native walk is strictly bounded.
                return ApplyDetectionMargin(WindowControlLocator.Find(target, _pointer)?.Bounds.Intersect(basis) ??
                                            basis);
            }
            catch (Exception)
            {
                return ApplyDetectionMargin(basis);
            }
        }

        private void EndSelection()
        {
            _state.Move(_mouse, _pointer);
            var dragged = _state.DragThresholdReached;
            _selectionIsWholeTarget = !_state.DragThresholdReached && _selectedTarget is { } target &&
                                      _pressRegion == (WindowSource ? target.Client : target.Bounds);
            _state.End(_mouse, _pointer, _pressRegion);
            if (dragged && GeometrySizeFixed) _state.Set(FitFixedGeometry(_state.Selected));
            CompleteOutsideExpansion(dragged);
            if (ShapeMode == SelectionShapeMode.MultiWindow && _regionEditBase is null && !_state.Selected.IsEmpty)
            {
                _selectionShape = CurrentSelectionShape();
                if (_selectedTarget is { } selected) AddSelectionWindow(selected);
            }

            if (_regionEditBase is not null)
            {
                using var path = new SKPath();
                var region = _state.Selected;
                path.AddRect(new(region.X, region.Y, region.Right, region.Bottom));
                CommitShape(path);
                return;
            }

            PrepareWindowScreenshot();
            QueueAutoConfirm();
        }

        private void Confirm()
        {
            CommitSelectionManipulation();
            if (_state.Dragging) EndSelection();
            var snapshot = Snapshot;
            var result =
                SelectionResult.Create(snapshot.Region, WindowSource, snapshot.Target, UsesLocalSource, _cursor,
                    _selectionShape,
                    ShapeMode == SelectionShapeMode.MultiWindow ? SelectedShapeWindows() : null);
            if (result is not null) RequestFinish(result);
        }

        private void RequestFinish(SelectionResult? result)
        {
            if (AllInputReleased)
            {
                CompleteSelection(result);
                return;
            }

            _pendingFinish = true;
            _pendingResult = result;
        }

        private void Refresh()
        {
            if (_finished) return;
            var snapshot = Snapshot;
            UpdateWindowOverlayFrame();
            if (snapshot != _lastSnapshot)
            {
                _lastSnapshot = snapshot;
                SelectionChanged?.Invoke(snapshot);
            }

            UpdateFrameDecorationPreview();
            UpdateCodeDetection();
            OverlayWindow? magnifierSource = null;
            // ReSharper disable once ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator
            foreach (var window in _windows)
            {
                if (!window.Region.Contains(_mouse)) continue;
                magnifierSource = window;
                break;
            }

            foreach (var window in _windows)
            {
                window.SetMagnifierPlacement(_pointer, magnifierSource);
                window.Refresh(snapshot.Region, _mouse, Mode);
            }
        }

        private void ReleaseCaptures()
        {
            _releasingCapture = true;
            try
            {
                foreach (var window in _windows) window.ReleaseCapture();
            }
            finally
            {
                _releasingCapture = false;
            }
        }

        public void Cancel()
        {
            if (!Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.Post(() => Finish(null));
            else Finish(null);
        }

        internal void CommitSource(CancellationToken token)
        {
            _sourceSelection?.Commit(token);
        }

        private void Finish(SelectionResult? result, Exception? error = null)
        {
            if (_finished) return;
            SelectionLogger.ZLogInformation(
                $"Selection finishing. SelectionId={_selectionId} Accepted={result is not null} HasError={error is not null} Scrolling={_scrolling} OverlayCount={_windows.Count}");
            if (error is not null)
                SelectionLogger.ZLogError(error, $"Selection completion failure. SelectionId={_selectionId}");
            _finished = true;
            _windowMonitor?.Dispose();
            _windowMonitor = null;
            DisposeCodeDetection();
            DisposeWindowsKeyGuard();
            StopFrameRefresh();
            _shiftTimer?.Dispose();
            _shiftTimer = null;
            _screens.Changed -= ScreensChanged;
            try
            {
                _cancel.Cancel();
            }
            catch (Exception cancellationError)
            {
                error ??= cancellationError;
            }

            error = CloseOverlays(error);
            if (error is not null) _completion.TrySetException(error);
            else _completion.TrySetResult(result);
        }

        private Exception? CloseOverlays(Exception? error = null)
        {
            foreach (var window in _windows)
                try
                {
                    window.ReleaseCapture();
                    window.Close();
                }
                catch (Exception closeError)
                {
                    error ??= closeError;
                }
                finally
                {
                    try
                    {
                        window.DisposeImage();
                    }
                    catch (Exception disposeError)
                    {
                        error ??= disposeError;
                    }
                }

            _windows.Clear();
            return error;
        }
    }
}
