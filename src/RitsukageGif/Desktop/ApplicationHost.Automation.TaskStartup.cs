using System.Diagnostics;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private Process? _taskStartupParent;
        private bool _taskStartupReceived;

        private void InitializeTaskStartupGuard(TaskHostOptions options)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (options.ParentProcessId is not { } processId) return;
            try
            {
                _taskStartupParent = Process.GetProcessById(processId);
                _taskStartupParent.Exited += TaskStartupParentExited;
                _taskStartupParent.EnableRaisingEvents = true;
                if (_taskStartupParent.HasExited) TaskStartupParentExited(null, EventArgs.Empty);
            }
            catch (ArgumentException)
            {
                TaskStartupParentExited(null, EventArgs.Empty);
            }
        }

        private void CompleteTaskStartupGuard()
        {
            Dispatcher.UIThread.VerifyAccess();
            _taskStartupReceived = true;
            if (_taskStartupParent is not { } parent) return;
            _taskStartupParent = null;
            parent.Exited -= TaskStartupParentExited;
            parent.Dispose();
        }

        private void TaskStartupParentExited(object? sender, EventArgs args)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_taskStartupReceived && !IsClosing) _ = ShutdownAsync();
            });
        }
    }
}
