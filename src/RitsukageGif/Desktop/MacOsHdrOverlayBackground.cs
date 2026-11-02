using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe class MacOsHdrOverlayBackground : IHdrOverlayBackground
    {
        private static readonly ConcurrentDictionary<nint, WeakReference<MacOsHdrOverlayBackground>> Hosts = new();
        private static long _nextId;
        private readonly PixelRect _bounds;
        private readonly nint _id;
        private readonly MacOsHdrPresenter _presenter;
        private nint _panel;

        internal MacOsHdrOverlayBackground(nint parentWindow, PixelRect bounds)
        {
            _bounds = bounds;
            _id = (nint)Interlocked.Increment(ref _nextId);
            Hosts[_id] = new(this);
            try
            {
                _presenter = new(&DisplayColorChanged, _id);
                _panel = _presenter.CreateOverlay(parentWindow);
            }
            catch
            {
                _presenter?.Dispose();
                Hosts.TryRemove(_id, out _);
                throw;
            }
        }

        public event Action? DisplayChanged;
        public bool SupportsHdr => _presenter.SupportsHdr;
        public bool SupportsTransparentInput => true;

        public void InvalidateDevice()
        {
        }

        public Task PresentAsync(CapturedFrame frame, CapturedFrame? windowFrame, PixelRect windowBounds,
            CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            token.ThrowIfCancellationRequested();
            _presenter.SetPatch(windowFrame, windowBounds, _bounds);
            _presenter.Present(frame.Pixels.Span, frame.Width, frame.Height,
                frame.ColorInfo.SdrWhiteNits / FrameColorInfo.ScRgbReferenceWhiteNits, frame.AlphaMode, frame.Stride);
            return Task.CompletedTask;
        }

        public void SetSelection(PixelRect selection)
        {
            _presenter.SetSelection(selection.Intersect(_bounds), _bounds);
        }

        public void Show()
        {
            MacOsHdrPresenter.ShowOverlay(_panel, 1);
        }

        public void Hide()
        {
            if (_panel != 0) MacOsHdrPresenter.ShowOverlay(_panel, 0);
        }

        public void Dispose()
        {
            if (_panel == 0) return;
            MacOsHdrPresenter.DestroyOverlay(_panel);
            _panel = 0;
            _presenter.Dispose();
            Hosts.TryRemove(_id, out _);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void DisplayColorChanged(nint id)
        {
            if (Hosts.TryGetValue(id, out var reference) && reference.TryGetTarget(out var host))
                Dispatcher.UIThread.Post(() =>
                {
                    if (host._panel != 0) host.DisplayChanged?.Invoke();
                });
        }
    }
}
