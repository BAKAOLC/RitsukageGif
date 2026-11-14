using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Avalonia.X11.Selections.Clipboard
{
    internal sealed class EventStreamWindow : IXEventWaiter
    {

        private readonly AvaloniaX11Platform _platform;

        public IntPtr Handle { get; private set; }

        private readonly List<(Func<XEvent, bool> filter, TaskCompletionSource<XEvent?> tcs, TimeSpan timeout)>
            _listeners = new();

        // We are adding listeners to an intermediate collection to avoid freshly added listeners to be called
        // in the same event loop iteration and potentially processing an event that was not meant for them.
        private readonly List<(Func<XEvent, bool> filter, TaskCompletionSource<XEvent?> tcs, TimeSpan timeout)>
            _addedListeners = new();
        private readonly DispatcherTimer _timeoutTimer;

        private readonly bool _isForeign;

        public EventStreamWindow(AvaloniaX11Platform platform, IntPtr? foreignWindow = null)
        {
            _platform = platform;
            if (foreignWindow.HasValue)
            {
                _isForeign = true;
                Handle = foreignWindow.Value;
                _platform.Windows[Handle] = new(OnEvent, null);
            }
            else
            {
                Handle = XLib.CreateEventWindow(platform, OnEvent);
            }

            _timeoutTimer = new(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTimer);
        }
        private static readonly Stopwatch _time = Stopwatch.StartNew();

        public Task<XEvent?> WaitForEventAsync(Func<XEvent, bool> predicate, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<XEvent?>();
            _addedListeners.Add((predicate, tcs, _time.Elapsed + timeout));

            _timeoutTimer.Start();
            return tcs.Task;
        }

        public void Dispose()
        {
            _timeoutTimer.Stop();

            _platform.Windows.Remove(Handle);
            if (_isForeign)
                XLib.XSelectInput(_platform.Display, Handle, IntPtr.Zero);
            else
                XLib.XDestroyWindow(_platform.Display, Handle);

            Handle = IntPtr.Zero;
            var toDispose = _listeners.ToList();
            toDispose.AddRange(_addedListeners);
            _listeners.Clear();
            _addedListeners.Clear();
            foreach (var l in toDispose)
                l.tcs.SetResult(null);
        }

        private void MergeListeners()
        {
            _listeners.AddRange(_addedListeners);
            _addedListeners.Clear();
        }

        private void OnTimer(object? sender, EventArgs eventArgs)
        {
            MergeListeners();
            for (var i = 0; i < _listeners.Count; i++)
            {
                var (filter, tcs, timeout) = _listeners[i];
                if (timeout < _time.Elapsed)
                {
                    _listeners.RemoveAt(i);
                    i--;
                    tcs.SetResult(null);
                }
            }

            if (_listeners.Count == 0)
                _timeoutTimer.Stop();
        }

        private void OnEvent(ref XEvent xev)
        {
            MergeListeners();
            for (var i = 0; i < _listeners.Count; i++)
            {
                var (filter, tcs, timeout) = _listeners[i];
                if (filter(xev))
                {
                    _listeners.RemoveAt(i);
                    i--;
                    tcs.SetResult(xev);
                }
            }
        }
    }
}
