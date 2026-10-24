using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class SelectionCoordinator : IDisposable
    {
        private readonly TaskCompletionSource<SelectionResult?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Screens _screens;
        private readonly int _radius, _threshold;
        private readonly SelectionState _state = new();
        private IReadOnlyList<WindowTarget> _targets = [];
        private WindowTarget? _hoveredTarget, _selectedTarget;
        private bool _controls, _controlPending;
        private PixelRect _controlBounds;
        private PixelPoint _controlPoint;
        private nint _controlWindow;
        private bool _windowSource;
        private readonly bool _cursor;
        private readonly int _fps;
        private readonly CancellationTokenSource _cancel = new();
        private readonly List<OverlayWindow> _windows = [];
        private PixelRect _hovered;
        private bool _left, _right, _escape, _enter, _shift, _finished, _started, _showing, _focusCheckPending;
        private int _mode = 3;
        private PixelPoint _mouse, _pointer;
        private long _shiftDown;
        private bool _tab, _sourceKey, _rightConfirm;
        private bool _pendingFinish;
        private SelectionResult? _pendingResult;
        private IDisposable? _shiftTimer;
        private NativeCaptureSelection? _sourceSelection;

        public SelectionCoordinator(Screens screens, int radius, int threshold, bool windowSource = false,
            bool cursor = false, int fps = 20)
        {
            _screens = screens;
            _windowSource = windowSource;
            _cursor = cursor;
            _fps = fps;
            _radius = radius;
            _threshold = threshold;
        }

        public async Task<SelectionResult?> ShowAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_started) throw new InvalidOperationException();
            _started = true;
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
                Finish(null, error);
                return await _completion.Task;
            }
            finally
            {
                Finish(null);
                _screens.Changed -= ScreensChanged;
                _cancel.Dispose();
            }
        }

        private async Task InitializeAsync()
        {
            if (DesktopCapture.UsesSourceSelection)
            {
                _sourceSelection =
                    await DesktopCapture.BeginSourceSelectionAsync(_windowSource, _cancel.Token, _cursor, _fps);
                var source = _sourceSelection.Frame;
                if (_finished) return;
                var region = new PixelRect(0, 0, source.Width, source.Height);
                _hovered = region;
                var working = _screens.Primary?.WorkingArea;
                var scale = Math.Max(1, Math.Max(region.Width / ((working?.Width ?? 1280) * .9),
                    region.Height / ((working?.Height ?? 800) * .85)));
                _windows.Add(new OverlayWindow(this, region, scale, source.Pixels, _radius, _threshold, true));
            }
            else
            {
                if (OperatingSystem.IsMacOS()) await Task.Run(MacWindowSystem.EnsurePermission, _cancel.Token);
                if (_finished) return;
                _targets = await Task.Run(WindowLocator.List,
                    _cancel.Token);
                if (_finished) return;
                foreach (var screen in _screens.All.ToArray())
                {
                    var b = screen.Bounds;
                    var region = new PixelRect(b.X, b.Y, b.Width, b.Height);
                    var pixels = await Task.Run(() =>
                    {
                        using var capture = new DesktopCapture(new RecordingOptions(region, Divisor: 1));
                        return capture.Capture();
                    }, _cancel.Token);
                    if (_finished) return;
                    _windows.Add(new OverlayWindow(this, region, screen.Scaling, pixels, _radius, _threshold));
                }
            }

            if (_finished) return;
            if (_windows.Count == 0)
            {
                Finish(null);
                return;
            }

            _showing = true;
            try
            {
                foreach (var window in _windows.ToArray())
                {
                    if (_finished) return;
                    window.Show();
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

                (active ?? _windows[0]).Activate();
            }
            finally
            {
                _showing = false;
                FocusChanged();
            }
        }

        private void ScreensChanged(object? sender, EventArgs args) => Cancel();

        internal void FocusChanged()
        {
            if (_finished || _showing || _focusCheckPending) return;
            _focusCheckPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _focusCheckPending = false;
                if (!_finished && !_showing && !_windows.Any(window => window.IsActive)) Finish(null);
            }, DispatcherPriority.Background);
        }

        internal void CaptureLost()
        {
            if (_finished) return;
            _left = _right = false;
            _rightConfirm = false;
            if (_pendingFinish)
            {
                Finish(null);
                return;
            }

            if (!_state.Dragging) return;
            _state.Clear();
            Refresh();
        }

        internal void PointerInput(PixelPoint point, bool left, bool right, bool shift, bool pressed = false)
        {
            if (_finished) return;
            try
            {
                if (_pendingFinish)
                {
                    _left = left;
                    _right = right;
                    if (!left && !right) Finish(_pendingResult);
                    return;
                }

                _pointer = point;
                UpdateShift(shift);
                UpdatePoint();
                if (pressed && left && !_left)
                {
                    _selectedTarget = _hoveredTarget;
                    _state.Begin(_mouse);
                }

                if (_state.Dragging) _state.Move(_mouse);
                if (!left && _left && _state.Dragging) EndSelection();
                if (pressed && right && !_right) _rightConfirm = true;
                var confirm = !right && _right && _rightConfirm;
                _left = left;
                _right = right;
                if (!right) _rightConfirm = false;
                if (confirm) Confirm();
                if (!left && !right)
                    foreach (var window in _windows)
                        window.ReleaseCapture();
                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        internal void KeyInput(Key key, bool down, bool shift)
        {
            if (_finished) return;
            try
            {
                UpdateShift(shift);
                UpdatePoint();
                if (key == Key.Tab)
                {
                    if (down && !_tab) ToggleControls();
                    _tab = down;
                }
                else if (key == Key.W)
                {
                    if (down && !_sourceKey) ToggleSource();
                    _sourceKey = down;
                }
                else if (key == Key.Enter)
                {
                    if (down && !_enter) Confirm();
                    _enter = down;
                }
                else if (key == Key.Escape)
                {
                    if (down && !_escape)
                    {
                        if (_state.Selected.IsEmpty && !_state.Dragging) RequestFinish(null);
                        else ClearSelection();
                    }

                    _escape = down;
                }
                else if (down && key is Key.Delete or Key.Back) ClearSelection();

                Refresh();
            }
            catch (Exception error)
            {
                Finish(null, error);
            }
        }

        private int Mode => _shift && Stopwatch.GetElapsedTime(_shiftDown).TotalMilliseconds >= 200 ? 0 : _mode;

        private void ClearSelection()
        {
            _state.Clear();
            _selectedTarget = null;
            _rightConfirm = false;
            _pendingFinish = false;
            _pendingResult = null;
            foreach (var window in _windows) window.ReleaseCapture();
        }

        private void UpdateShift(bool shift)
        {
            if (shift == _shift) return;
            _shiftTimer?.Dispose();
            _shiftTimer = null;
            if (shift)
            {
                _shiftDown = Stopwatch.GetTimestamp();
                _shiftTimer = DispatcherTimer.RunOnce(() =>
                {
                    if (_finished || !_shift) return;
                    try
                    {
                        UpdatePoint();
                        if (_state.Dragging) _state.Move(_mouse);
                        Refresh();
                    }
                    catch (Exception error)
                    {
                        Finish(null, error);
                    }
                }, TimeSpan.FromMilliseconds(200));
            }
            else if (Stopwatch.GetElapsedTime(_shiftDown).TotalMilliseconds < 200) _mode = (_mode + 1) % 4;

            _shift = shift;
        }

        internal bool Controls => _controls;
        internal bool WindowSource => _windowSource;
        internal bool CanSwitchSource => !DesktopCapture.UsesSourceSelection;
        internal bool CanDetectControls => CanSwitchSource && OperatingSystem.IsWindows();

        internal void ToggleControls()
        {
            if (!CanDetectControls || _state.Dragging || _finished) return;
            _controls = !_controls;
            _controlBounds = default;
            UpdatePoint();
            Refresh();
        }

        internal void ToggleSource()
        {
            if (!CanSwitchSource || _state.Dragging || _finished) return;
            _windowSource = !_windowSource;
            UpdatePoint();
            Refresh();
        }

        private void UpdatePoint()
        {
            if (!DesktopCapture.UsesSourceSelection)
            {
                _hoveredTarget = _targets.FirstOrDefault(candidate => candidate.Bounds.Contains(_pointer));
                _hovered = _hoveredTarget is { } target
                    ? _windowSource ? target.Client : target.Bounds
                    : default;
                if (_controls && _hoveredTarget is { } controlTarget)
                {
                    if (_controlWindow == controlTarget.Handle && _controlPoint == _pointer && !_controlBounds.IsEmpty)
                        _hovered = _controlBounds.Intersect(_hovered);
                    else if (!_controlPending) _ = DetectControlAsync(controlTarget, _pointer);
                }
            }

            var current = _windows.FirstOrDefault(window => window.Region.Contains(_pointer));
            _mouse = current?.Snap(_pointer, Mode) ?? _pointer;
        }

        private async Task DetectControlAsync(WindowTarget target, PixelPoint point)
        {
            _controlPending = true;
            try
            {
                var token = _cancel.Token;
                var result = await Task.Run(() => WindowControlLocator.Find(target, point, token), token);
                if (_finished) return;
                _controlWindow = target.Handle;
                _controlPoint = point;
                _controlBounds = result is not null && NativeMethods.IsWindowVisible(result.Handle) != 0
                    ? result.Bounds.Intersect(result.Window.Client)
                    : default;
                if (_controls && _pointer == point && _hoveredTarget?.Handle == target.Handle &&
                    !_controlBounds.IsEmpty)
                {
                    _hovered = _controlBounds.Intersect(_windowSource ? target.Client : target.Bounds);
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
            }

            if (!_finished && _controls && _hoveredTarget is { } current &&
                (_pointer != point || current.Handle != target.Handle))
                _ = DetectControlAsync(current, _pointer);
        }

        private PixelRect ResolveClickTarget(WindowTarget? target)
        {
            if (!_controls || target is null) return _hovered;
            var basis = _windowSource ? target.Client : target.Bounds;
            try
            {
                // Resolve a quick click before committing it; the native walk is strictly bounded.
                return WindowControlLocator.Find(target, _pointer)?.Bounds.Intersect(basis) ?? basis;
            }
            catch (Exception)
            {
                return basis;
            }
        }

        private void EndSelection()
        {
            _state.End(_mouse, _mouse == _state.Anchor
                ? ResolveClickTarget(_selectedTarget)
                : _hovered);
        }

        private void Confirm()
        {
            if (_state.Dragging) EndSelection();
            if (_state.Selected.IsEmpty) _hovered = ResolveClickTarget(_hoveredTarget);
            var selected = _state.Selected.IsEmpty ? _hovered : _state.Selected;
            var target = _state.Selected.IsEmpty ? _hoveredTarget : _selectedTarget;
            var result = SelectionResult.Create(selected, _windowSource, target, DesktopCapture.UsesSourceSelection);
            if (result is not null) RequestFinish(result);
        }

        private void RequestFinish(SelectionResult? result)
        {
            if (!_left && !_right)
            {
                Finish(result);
                return;
            }

            _pendingFinish = true;
            _pendingResult = result;
        }

        private void Refresh()
        {
            if (_finished) return;
            var display = _state.Selected.IsEmpty && !_state.Dragging ? _hovered : _state.Selected;
            if (_windowSource && !DesktopCapture.UsesSourceSelection)
                display = (_state.Selected.IsEmpty && !_state.Dragging ? _hoveredTarget : _selectedTarget) is { } target
                    ? display.Intersect(target.Client)
                    : default;
            foreach (var window in _windows) window.Refresh(display, _mouse, Mode);
        }

        public void Cancel()
        {
            if (!Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.Post(() => Finish(null));
            else Finish(null);
        }

        internal void CommitSource(CancellationToken token) => _sourceSelection?.Commit(token);

        public void Dispose()
        {
            Cancel();
            _sourceSelection?.Dispose();
            _sourceSelection = null;
        }

        private void Finish(SelectionResult? result, Exception? error = null)
        {
            if (_finished) return;
            _finished = true;
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

            foreach (var window in _windows)
            {
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
            }

            _windows.Clear();
            if (error is not null) _completion.TrySetException(error);
            else _completion.TrySetResult(result);
        }
    }
}
