using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionWindowMonitor : IDisposable
    {
        private static readonly ILogger Logger = LogHub.GetLogger("Desktop.Selection.Windows");
        private readonly ForegroundHotkeyCallback _callback;
        private readonly Action _changed;
        private readonly List<nint> _hooks = [];
        private bool _disposed;
        private bool _queued;

        internal SelectionWindowMonitor(Action changed)
        {
            Dispatcher.UIThread.VerifyAccess();
            _changed = changed;
            _callback = OnEvent;
            try
            {
                AddHook(0x0003, 0x0003);
                AddHook(0x000B, 0x000B);
                AddHook(0x0016, 0x0017);
                AddHook(0x0020, 0x0020);
                AddHook(0x8000, 0x8018);
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
                    Logger.ZLogWarning($"Selection window event hook removal failed.");
            _hooks.Clear();
            GC.KeepAlive(_callback);
        }

        private void AddHook(uint first, uint last)
        {
            var hook = SetWinEventHook(first, last, 0, Marshal.GetFunctionPointerForDelegate(_callback), 0, 0, 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            _hooks.Add(hook);
        }

        private void OnEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint threadId,
            uint time)
        {
            if (_disposed || _queued) return;
            if (TransientWindowShell.IsTransient(window)) return;
            if (eventType >= 0x8000 && (objectId != 0 || childId != 0 || eventType is not
                    (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x8004 or 0x800A or 0x800B or 0x800C or 0x800F or
                    0x8017 or 0x8018))) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                if (!_disposed) _changed();
            }, DispatcherPriority.Background);
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint SetWinEventHook(uint minimum, uint maximum, nint module, nint callback,
            uint processId, uint threadId, uint flags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnhookWinEvent(nint hook);
    }
}
