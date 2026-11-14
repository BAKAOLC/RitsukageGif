using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowLocator
    {
        internal static WindowTarget? FindLivePinAtPoint(PixelPoint point)
        {
            if (!OperatingSystem.IsWindows())
                return List().FirstOrDefault(target => target.ProcessId != Environment.ProcessId &&
                                                       target.Bounds.Contains(point));

            var previous = SetCandidateDpiContext(-4);
            try
            {
                var hit = WindowFromPoint(new() { X = point.X, Y = point.Y });
                if (hit == 0) return null;
                var root = GetAncestor(hit, 2);
                if (root == 0) root = hit;
                if (GetWindowThreadProcessId(root, out var processId) == 0 || processId == Environment.ProcessId)
                    return null;
                var target = new WindowTarget(root, string.Empty, string.Empty, default, default)
                {
                    ProcessId = processId,
                };
                if (!IsCandidate(target) || Refresh(target) is not { } current || !current.Bounds.Contains(point))
                    return null;
                try
                {
                    using var owner = Process.GetProcessById((int)processId);
                    return current with { Process = owner.ProcessName, Title = ReadLivePinTitle(root) };
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    return null;
                }
            }
            finally
            {
                if (previous != 0) SetCandidateDpiContext(previous);
            }
        }

        internal static nint GlobalMouseWindowAtPoint(PixelPoint point)
        {
            var previous = SetCandidateDpiContext(-4);
            try
            {
                var hit = WindowFromPoint(new() { X = point.X, Y = point.Y });
                if (hit == 0) return 0;
                var root = GetAncestor(hit, 2);
                return root == 0 ? hit : root;
            }
            finally
            {
                if (previous != 0) SetCandidateDpiContext(previous);
            }
        }

        private static unsafe string ReadLivePinTitle(nint window)
        {
            const int capacity = 1024;
            var buffer = stackalloc char[capacity];
            return new(buffer, 0, GetWindowText(window, buffer, capacity));
        }

        [LibraryImport("user32.dll")]
        private static partial nint WindowFromPoint(NativeMethods.Point point);

        [LibraryImport("user32.dll")]
        private static partial nint GetAncestor(nint window, uint flags);
    }
}
