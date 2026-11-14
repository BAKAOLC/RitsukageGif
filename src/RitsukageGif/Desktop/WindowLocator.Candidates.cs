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
            if (!IsCandidate(target) || IsFullyTransparentCandidate(target.Handle)) return null;
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
                    if (!TryGetCandidateProcess(handle, out var process) || !TryGetFrame(handle, out var frame))
                        continue;
                    var bounds = frame.Pixels;
                    if (bounds.IsEmpty || !bounds.Contains(point) || IsFullyTransparentCandidate(handle)) continue;
                    WindowTarget? candidate = null;
                    // ReSharper disable once ForCanBeConvertedToForeach
                    for (var index = 0; index < candidates.Count; index++)
                    {
                        if (candidates[index].Handle != handle) continue;
                        candidate = candidates[index];
                        break;
                    }

                    if (candidate is not null && !MatchesCandidateProcess(candidate, process)) continue;
                    if (!TryGetClient(handle, out var client)) continue;
                    if (candidate is not null) return candidate with { Bounds = bounds, Client = client };
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
            return TryGetCandidateProcess(target.Handle, out var process) && MatchesCandidateProcess(target, process);
        }

        private static bool MatchesCandidateProcess(WindowTarget target, uint process)
        {
            return target.ProcessId == 0 || target.ProcessId == process;
        }

        private static bool IsFullyTransparentCandidate(nint handle)
        {
            const int extendedStyle = -20;
            const int layered = 0x80000;
            const uint alphaFlag = 2;
            return (GetCandidateWindowLong(handle, extendedStyle) & layered) != 0 &&
                   GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags) &&
                   (flags & alphaFlag) != 0 && alpha == 0;
        }

        private static bool TryGetCandidateProcess(nint handle, out uint process)
        {
            process = 0;
            if (NativeMethods.IsWindowVisible(handle) == 0 || NativeMethods.IsIconic(handle) != 0 ||
                GetWindowThreadProcessId(handle, out process) == 0 || process == Environment.ProcessId) return false;
            return GetCandidateCloak(handle, 14, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
        }

        [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static partial int GetCandidateCloak(nint window, uint attribute, out int value, int size);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetCandidateWindowLong(nint window, int index);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLayeredWindowAttributes(nint window, out uint color, out byte alpha,
            out uint flags);

        [LibraryImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext")]
        private static partial nint SetCandidateDpiContext(nint context);
    }
}
