using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingAnnotationOverlay(ScreenshotAnnotationSession session) : IDisposable
    {
        private readonly List<(Window Window, ScreenshotAnnotationSurface Surface)> _windows = [];
        private (PixelRect Local, double Scale)[] _viewports = [];
        private PixelRect _region;
        private nint[] _inputStyles = [];
        private bool _interactive;
        private bool _visible;

        public void Dispose()
        {
            CloseWindows();
        }

        internal event Action<KeyEventArgs>? KeyPressed;
        internal event Action<KeyEventArgs>? KeyReleased;
        internal event Action? FocusLost;

        internal bool Owns(TopLevel? window)
        {
            return _windows.Any(item => item.Window == window);
        }

        internal void Update(Window owner, PixelRect region, bool visible, bool sourceExcludesOverlay)
        {
            _visible = visible;
            if (!visible)
            {
                foreach (var (window, _) in _windows) window.Hide();
                return;
            }

            var placements = owner.Screens.All.Select(screen =>
            {
                var clip = region.Intersect(new(screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height));
                return (Global: clip, Local: new PixelRect(clip.X - region.X, clip.Y - region.Y,
                    clip.Width, clip.Height), Scale: OperatingSystem.IsMacOS() ? 1 : screen.Scaling);
            }).Where(item => !item.Global.IsEmpty).ToArray();
            var viewports = placements.Select(item => (item.Local, item.Scale)).ToArray();
            _region = region;
            if (_windows.Count != 0 && _viewports.SequenceEqual(viewports))
            {
                for (var index = 0; index < _windows.Count; index++)
                {
                    var window = _windows[index].Window;
                    var placement = placements[index];
                    window.Position = new(placement.Global.X, placement.Global.Y);
                    if (!window.IsVisible) window.Show();
                }

                UpdateInput();
                return;
            }

            if (session.IsCanvasTextEditing) session.CommitTextEdit();
            CloseWindows();
            _viewports = viewports;
            foreach (var (clip, local, scale) in placements)
            {
                var window = new Window
                {
                    WindowDecorations = WindowDecorations.None, ShowActivated = false, Topmost = true,
                    CanResize = false, MinWidth = 0, MinHeight = 0, Opacity = 0,
                    Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                    TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new(clip.X, clip.Y), Width = clip.Width / scale,
                    Height = clip.Height / scale,
                };
                if (OperatingSystem.IsLinux()) window.Title = LinuxCaptureExclusionLease.CreateWindowMarker();
                var surface = new ScreenshotAnnotationSurface(session, local, scale, args =>
                {
                    var point = window.PointToScreen(args.GetPosition(window));
                    return new(point.X - _region.X, point.Y - _region.Y);
                });
                var exposed = !OperatingSystem.IsLinux() ||
                              _nativeProof?.Source == LinuxCaptureExclusionSource.SelectedWindow;
                window.Content = exposed ? surface : null;
                TransientWindowShell.Configure(window);
                window.KeyDown += (_, args) => KeyPressed?.Invoke(args);
                window.KeyUp += (_, args) => KeyReleased?.Invoke(args);
                window.Deactivated += (_, _) => FocusLost?.Invoke();
                window.Show();
                if (!OperatingSystem.IsLinux() && !sourceExcludesOverlay && !OperatingSystem.IsMacOS() &&
                    !RecordingRegionOverlay.Exclude(window))
                {
                    surface.Dispose();
                    window.Close();
                    CloseWindows();
                    throw new NotSupportedException(UiText.Get("CaptureFailed"));
                }

                window.Opacity = exposed ? 1 : 0;
                if (exposed && OperatingSystem.IsLinux()) _nativeProtected.Add(window);
                _windows.Add((window, surface));
            }

            _inputStyles = new nint[_windows.Count];
            UpdateInput(true);
            ResumeNativeExclusion();
        }

        internal void UpdateInput(bool force = false)
        {
            var interactive = session.CanAnnotate &&
                              (session.Tool != ScreenshotTool.Select || session.IsEditingText || session.IsDrawing);
            if (!force && interactive == _interactive) return;
            _interactive = interactive;
            for (var i = 0; i < _windows.Count; i++)
            {
                var (window, surface) = _windows[i];
                var acceptsInput = interactive && ReferenceEquals(window.Content, surface);
                window.IsHitTestVisible = acceptsInput;
                PinnedWindowPlatform.SetMouseThrough(window, !acceptsInput, ref _inputStyles[i]);
            }
        }

        private void CloseWindows()
        {
            foreach (var (window, surface) in _windows)
            {
                surface.Dispose();
                window.Close();
                _nativeProtected.Remove(window);
                if (_nativeLeases.Remove(window, out var lease)) QueueNativeRelease(lease);
            }

            _windows.Clear();
            _inputStyles = [];
        }
    }
}
