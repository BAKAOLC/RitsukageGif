using System.Collections.Concurrent;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        private const int ResponseTimeoutMilliseconds = 120;
        private const int WorkBudgetMilliseconds = 80;
        private static readonly Lazy<BlockingCollection<Request>> Requests = new(CreateWorker);

        internal static async Task<IReadOnlyList<WindowControlTarget>> FindHierarchyAsync(WindowTarget window,
            PixelPoint point, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() || window.Bounds.IsEmpty || !window.Bounds.Contains(point) ||
                IsWindowVisible(window.Handle) == 0 || IsIconic(window.Handle) != 0 ||
                GetWindowThreadProcessId(window.Handle, out var process) == 0 || process == Environment.ProcessId)
                return [];

            var request = new Request(window, point, Stopwatch.GetTimestamp(), cancellationToken);
            if (!Requests.Value.TryAdd(request)) return [];
            try
            {
                return await request.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(ResponseTimeoutMilliseconds),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return [];
            }
        }

        private static BlockingCollection<Request> CreateWorker()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var queue = new BlockingCollection<Request>(1);
            var thread = new Thread(() => RunWorker(queue))
                { IsBackground = true, Name = "Window accessibility detection" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return queue;
        }

        private static bool Expired(Request request)
        {
            return request.CancellationToken.IsCancellationRequested ||
                   Stopwatch.GetElapsedTime(request.Started).TotalMilliseconds >= WorkBudgetMilliseconds;
        }
    }
}
