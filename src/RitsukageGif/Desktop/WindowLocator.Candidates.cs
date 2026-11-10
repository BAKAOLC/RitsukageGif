using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowLocator
    {
        internal static IReadOnlyList<WindowTarget> ListCandidates()
        {
            if (!OperatingSystem.IsWindows()) return List();
            var previous = SetCandidateDpiContext(-4);
            try
            {
                return [.. List().Where(IsCandidate)];
            }
            finally
            {
                if (previous != 0) SetCandidateDpiContext(previous);
            }
        }

        internal static WindowTarget? RefreshCandidate(WindowTarget target)
        {
            if (!OperatingSystem.IsWindows()) return target;
            if (!IsCandidate(target)) return null;
            var previous = SetCandidateDpiContext(-4);
            try
            {
                var current = Refresh(target);
                return current is not null && !current.Bounds.IsEmpty ? current : null;
            }
            finally
            {
                if (previous != 0) SetCandidateDpiContext(previous);
            }
        }

        internal static WindowTarget? CandidateAtPoint(IReadOnlyList<WindowTarget> candidates, PixelPoint point)
        {
            if (!OperatingSystem.IsWindows())
                return candidates.FirstOrDefault(candidate => candidate.Bounds.Contains(point));
            var previous = SetCandidateDpiContext(-4);
            try
            {
                var seen = new HashSet<nint>();
                for (var handle = NativeMethods.GetTopWindow(0);
                     handle != 0 && seen.Add(handle);
                     handle = NativeMethods.GetWindow(handle, 2))
                {
                    var candidate = candidates.FirstOrDefault(target => target.Handle == handle);
                    var current =
                        RefreshCandidate(candidate ?? new(handle, string.Empty, string.Empty, default, default));
                    if (current is null || !current.Bounds.Contains(point)) continue;
                    if (candidate is not null) return current;
                    // A newly shown window must not expose a stale candidate underneath it while inventory loads.
                    if (HasCandidateTitle(handle)) return null;
                }

                return null;
            }
            finally
            {
                if (previous != 0) SetCandidateDpiContext(previous);
            }
        }

        private static unsafe bool HasCandidateTitle(nint window)
        {
            var title = stackalloc char[2];
            return GetWindowText(window, title, 2) != 0;
        }

        private static bool IsCandidate(WindowTarget target)
        {
            if (NativeMethods.IsWindowVisible(target.Handle) == 0 || NativeMethods.IsIconic(target.Handle) != 0 ||
                GetWindowThreadProcessId(target.Handle, out var process) == 0 || process == Environment.ProcessId ||
                (target.ProcessId != 0 && process != target.ProcessId)) return false;
            return GetCandidateCloak(target.Handle, 14, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
        }

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static partial int GetCandidateCloak(nint window, uint attribute, out int value, int size);

        [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
        private static partial nint SetCandidateDpiContext(nint context);
    }
}
