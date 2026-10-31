using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SceneCaptureContext
    {
        // Cache source frames only within BeginFrame; each scene applies its own crop and tone mapping.
        private readonly Dictionary<(nint Handle, bool Cursor, int Fps), ColorWindowEntry> _colorWindows = [];

        public CapturedFrameSet? CaptureWindowFrames(nint handle, RecordingOptions options, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            var fps = _captureFps ?? options.Fps;
            var key = (handle, options.Cursor, fps);
            if (_unavailable.Contains(key)) return null;
            try
            {
                if (!_colorWindows.TryGetValue(key, out var entry))
                {
                    entry = new(_windowFactory(handle, options.Cursor, fps));
                    _colorWindows.Add(key, entry);
                }

                entry.LastUsed = MotionTime;
                if (entry.HasFrame) return entry.Frame;
                entry.Frame = entry.Capture is IColorFrameSource color
                    ? color.CaptureFrames(token)
                    : SdrFrame(entry.Capture, token);
                entry.HasFrame = true;
                return entry.Frame;
            }
            catch (COMException error) when (error.HResult == unchecked((int)0x80000013))
            {
                DropColorWindow(key);
                return null;
            }
            catch (CaptureSourceLostException)
            {
                DropColorWindow(key);
                return null;
            }
        }

        private static CapturedFrameSet? SdrFrame(ISceneWindowSource source, CancellationToken token)
        {
            var pixels = source.Capture(token);
            if (pixels is null) return null;
            var width = source.Width;
            var height = source.Height;
            var color = new FrameColorInfo(false);
            return new(new(width, height, checked(width * 4), CapturePixelFormat.Bgra8Srgb, pixels, color,
                    CaptureAlphaMode.Straight, source.FrameRevision, [new(new(0, 0, width, height), color)]),
                null, Stopwatch.GetTimestamp());
        }

        private void DropColorWindow((nint Handle, bool Cursor, int Fps) key)
        {
            if (_colorWindows.Remove(key, out var entry)) entry.Capture.Dispose();
            _unavailable.Add(key);
        }

        private void BeginColorFrame()
        {
            foreach (var (key, entry) in _colorWindows)
                if (MotionTime - entry.LastUsed > 5)
                {
                    _expired.Add(key);
                }
                else
                {
                    entry.Frame = null;
                    entry.HasFrame = false;
                }

            foreach (var key in _expired)
            {
                _colorWindows[key].Capture.Dispose();
                _colorWindows.Remove(key);
            }

            _expired.Clear();
        }

        private void DisposeColorWindows()
        {
            foreach (var entry in _colorWindows.Values) entry.Capture.Dispose();
            _colorWindows.Clear();
        }

        private sealed class ColorWindowEntry(ISceneWindowSource capture)
        {
            public readonly ISceneWindowSource Capture = capture;
            public CapturedFrameSet? Frame;
            public bool HasFrame;
            public double LastUsed;
        }
    }
}
