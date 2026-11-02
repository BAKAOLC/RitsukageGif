using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ForegroundHotkeyMonitor : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Input.Foreground");
        private readonly ForegroundHotkeyCallback _callback;
        private readonly Action _changed;
        private readonly List<nint> _hooks = [];
        private bool _disposed;
        private bool _queued;

        internal ForegroundHotkeyMonitor(Action changed)
        {
            _changed = changed;
            _callback = OnEvent;
            try
            {
                foreach (var eventType in new uint[] { 0x0003, 0x000B, 0x0017, 0x800B })
                {
                    var hook = SetWinEventHook(eventType, eventType, 0,
                        Marshal.GetFunctionPointerForDelegate(_callback), 0, 0, 0);
                    if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                    _hooks.Add(hook);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var hook in _hooks)
                if (!UnhookWinEvent(hook))
                    Logger.ZLogWarning($"Native foreground event hook removal failed.");
            _hooks.Clear();
            GC.KeepAlive(_callback);
        }

        private void OnEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId,
            uint time)
        {
            if (_disposed || _queued || window == 0 ||
                (eventType == 0x800B && (objectId != 0 || childId != 0 || window != GetForegroundWindow()))) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                if (!_disposed) _changed();
            });
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint SetWinEventHook(uint minimum, uint maximum, nint module,
            nint callback, uint processId, uint threadId, uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnhookWinEvent(nint hook);

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();
    }
}
