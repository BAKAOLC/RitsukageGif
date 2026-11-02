using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class HdrPreviewHost
    {
        private bool? _macHdr;

        private IPlatformHandle CreateMac(IPlatformHandle parent)
        {
            if (_disposed) return base.CreateNativeControlCore(parent);
            _subscription = (nuint)Interlocked.Increment(ref _nextSubscription);
            Subscriptions[_subscription] = new(this);
            try
            {
                _macPresenter = new(&MacDisplayChanged, (nint)_subscription);
                QueueRefresh();
                return new PlatformHandle(_macPresenter.View, "NSView");
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
                                              ExternalException or NotSupportedException)
            {
                DestroyMac();
                Trace.TraceWarning("macOS HDR preview unavailable: {0}", error.Message);
                return base.CreateNativeControlCore(parent);
            }
        }

        private void DestroyMac()
        {
            _macPresenter?.Dispose();
            _macPresenter = null;
            if (_subscription != 0) Subscriptions.TryRemove(_subscription, out _);
            _subscription = 0;
            _macHdr = null;
        }

        private void RefreshMac()
        {
            if (_failed || _macPresenter is null || _frame?.HdrPixels is not { } pixels)
            {
                SetPresentation(false);
                return;
            }

            var hdr = _macPresenter.SupportsHdr;
            if (_macHdr != hdr)
            {
                _macHdr = hdr;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed) DisplayColorChanged?.Invoke();
                }, DispatcherPriority.Render);
            }

            if (!hdr)
            {
                SetPresentation(false);
                return;
            }

            try
            {
                _macPresenter.Present(pixels.AsSpan(0, checked(_frame.Width * _frame.Height * 8)),
                    _frame.Width, _frame.Height, _frame.HdrReferenceWhiteLevel, _frame.HdrAlphaMode);
                SetPresentation(true);
            }
            catch (ExternalException error)
            {
                _failed = true;
                SetPresentation(false);
                Trace.TraceWarning("macOS HDR preview presentation failed: {0}", error.Message);
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void MacDisplayChanged(nint subscription)
        {
            if (Subscriptions.TryGetValue((nuint)subscription, out var reference) &&
                reference.TryGetTarget(out var host))
                Dispatcher.UIThread.Post(host.QueueRefresh, DispatcherPriority.Render);
        }
    }
}
