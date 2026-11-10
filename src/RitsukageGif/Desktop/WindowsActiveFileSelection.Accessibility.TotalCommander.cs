using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsActiveFileSelection
    {
        private static bool IsTotalCommander(string? program)
        {
            var name = Path.GetFileName(program.AsSpan());
            return name.Equals("TOTALCMD.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("TOTALCMD64.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static unsafe string? ReadTotalCommanderFolder(nint automation, nint root, nint view, uint processId,
            CancellationToken token)
        {
            var viewHandle = unchecked((uint)ReadAccessibilityInteger(view, 30020));
            if (viewHandle == 0) return null;
            var left = QueryTotalCommanderHandle(root, 1, token);
            var right = QueryTotalCommanderHandle(root, 2, token);
            var control = viewHandle == unchecked((uint)(nuint)left) && left != 0 ? 9u :
                viewHandle == unchecked((uint)(nuint)right) && right != 0 ? 10u : 0;
            if (control == 0) return null;
            var pathHandle = QueryTotalCommanderHandle(root, control, token);
            if (pathHandle == 0) return null;
            if (GetWindowThreadProcessId(pathHandle, out var owner) == 0 || owner != processId ||
                GetAncestor(pathHandle, 3) != root)
                throw new IOException("The Total Commander path control is no longer part of the invocation window.");
            nint pathElement = 0;
            try
            {
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)
                    (*(nint**)automation)[6])(automation, pathHandle, &pathElement));
                if (ReadAccessibilityInteger(pathElement, 30002) != processId ||
                    ReadAccessibilityBoolean(pathElement, 30022))
                    throw new IOException("The Total Commander path control is no longer available.");
                var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectAccessibleDirectoryValues(pathElement, directories, true);
                return directories.Count == 1
                    ? directories.First()
                    : throw new NotSupportedException(
                        "The Total Commander pane does not identify one filesystem folder.");
            }
            finally
            {
                WindowsShellDispatch.Release(pathElement);
            }
        }

        private static nint QueryTotalCommanderHandle(nint root, uint control, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (SendFileSelectionMessage(root, 0x432, control, 0, 3, 500, out var result) == 0)
                throw new IOException("Total Commander did not respond to the file-pane query.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            return (nint)result;
        }

        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
        private static partial nint SendFileSelectionMessage(nint window, uint message, nuint parameter, nint data,
            uint flags, uint timeout, out nuint result);
    }
}
