using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Desktop
{
    internal sealed partial class CommandPipe
    {
        private static void AllowServerActivation(SafePipeHandle pipe)
        {
            // The newly launched process grants foreground permission to the connected instance
            // before asking its UI thread to show the window.
            if (GetNamedPipeServerProcessId(pipe, out var processId) != 0 &&
                AllowSetForegroundWindow(processId) == 0)
                Trace.TraceInformation("Windows did not grant foreground activation permission to process {0}.",
                    processId);
        }

        [LibraryImport("kernel32.dll")]
        private static partial int GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

        [LibraryImport("user32.dll")]
        // ReSharper disable once UnusedMethodReturnValue.Local -- Keep the native ABI; Windows may deny activation.
        private static partial int AllowSetForegroundWindow(uint processId);
    }
}
