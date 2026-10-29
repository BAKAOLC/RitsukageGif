using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsAccessibilityLocator
    {
        private static WindowsFocusedElementIdentity? ReadFocusIdentity(nint automation, FocusRequest request)
        {
            nint element = 0, runtimeId = 0;
            try
            {
                if (FocusExpired(request) || FocusForegroundWindow() != request.Foreground ||
                    ReadObject(automation, 8, out element) < 0 || element == 0) return null;
                int process, focused;
                if (((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)element)[20])(element, &process) < 0 ||
                    process <= 0 || (uint)process != request.Process ||
                    ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)element)[26])(element, &focused) < 0 ||
                    focused == 0 || ReadObject(element, 4, out runtimeId) < 0 || runtimeId == 0 ||
                    SafeArrayGetDim(runtimeId) != 1 || SafeArrayGetElemsize(runtimeId) != sizeof(int) ||
                    SafeArrayGetVartype(runtimeId, out var type) < 0 || type != 3 ||
                    SafeArrayGetLBound(runtimeId, 1, out var lower) < 0 ||
                    SafeArrayGetUBound(runtimeId, 1, out var upper) < 0) return null;
                var length = (long)upper - lower + 1;
                if (length is <= 0 or > 128 || FocusExpired(request)) return null;
                if (SafeArrayAccessData(runtimeId, out var values) < 0) return null;
                WindowsFocusedElementIdentity identity;
                int unlocked;
                try
                {
                    if (values == 0) return null;
                    identity = new(request.Process, new((void*)values, (int)length));
                }
                finally
                {
                    unlocked = SafeArrayUnaccessData(runtimeId);
                }

                if (unlocked < 0)
                {
                    Trace.TraceWarning("Could not unlock the focused element runtime ID: 0x{0:X8}", unlocked);
                    return null;
                }

                return !FocusExpired(request) && FocusForegroundWindow() == request.Foreground &&
                       GetWindowThreadProcessId(request.Foreground, out var currentProcess) != 0 &&
                       currentProcess == request.Process &&
                       ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)element)[26])(element, &focused) >=
                       0 &&
                       focused != 0
                    ? identity
                    : null;
            }
            finally
            {
                if (runtimeId != 0)
                {
                    var destroyed = SafeArrayDestroy(runtimeId);
                    if (destroyed < 0)
                        Trace.TraceWarning("Could not release the focused element runtime ID: 0x{0:X8}", destroyed);
                }

                Release(element);
            }
        }

        [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static partial nint FocusForegroundWindow();

        [LibraryImport("oleaut32.dll")]
        private static partial uint SafeArrayGetDim(nint array);

        [LibraryImport("oleaut32.dll")]
        private static partial uint SafeArrayGetElemsize(nint array);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayGetVartype(nint array, out ushort type);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayGetLBound(nint array, uint dimension, out int lower);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayGetUBound(nint array, uint dimension, out int upper);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayAccessData(nint array, out nint data);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayUnaccessData(nint array);

        [LibraryImport("oleaut32.dll")]
        private static partial int SafeArrayDestroy(nint array);
    }
}
