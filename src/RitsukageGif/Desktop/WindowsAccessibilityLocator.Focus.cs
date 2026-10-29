using System.Collections.Concurrent;
using System.Diagnostics;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        private static readonly Lazy<BlockingCollection<FocusRequest>> FocusRequests = new(CreateFocusWorker);

        internal static async Task<WindowsFocusedElementIdentity?> ReadFocusedIdentityAsync(nint foreground,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() || foreground == 0 || FocusForegroundWindow() != foreground ||
                GetWindowThreadProcessId(foreground, out var process) == 0 || process == 0) return null;
            var request = new FocusRequest(foreground, process, Stopwatch.GetTimestamp(), token);
            if (!FocusRequests.Value.TryAdd(request)) return null;
            try
            {
                return await request.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(ResponseTimeoutMilliseconds),
                    token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        private static BlockingCollection<FocusRequest> CreateFocusWorker()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var queue = new BlockingCollection<FocusRequest>(1);
            var thread = new Thread(() => RunFocusWorker(queue))
                { IsBackground = true, Name = "Capture focus identity" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return queue;
        }

        private static bool FocusExpired(FocusRequest request)
        {
            return request.Token.IsCancellationRequested ||
                   Stopwatch.GetElapsedTime(request.Started).TotalMilliseconds >= WorkBudgetMilliseconds;
        }

        private static void RunFocusWorker(BlockingCollection<FocusRequest> queue)
        {
            var initialized = CoInitializeEx(0, 0);
            nint automation = 0;
            try
            {
                foreach (var request in queue.GetConsumingEnumerable())
                {
                    WindowsFocusedElementIdentity? result = null;
                    try
                    {
                        if (FocusExpired(request)) continue;
                        if (initialized < 0) continue;
                        if (automation == 0 && (CoCreateInstance(in AutomationClass, 0, 1, in AutomationInterface,
                                                    out automation) < 0 || automation == 0 ||
                                                Configure(automation, 59, 0) < 0 ||
                                                Configure(automation, 61, 50) < 0 || Configure(automation, 63, 50) < 0))
                        {
                            Release(automation);
                            automation = 0;
                            continue;
                        }

                        result = ReadFocusIdentity(automation, request);
                        if (FocusExpired(request)) result = null;
                    }
                    catch (Exception)
                    {
                        Release(automation);
                        automation = 0;
                    }
                    finally
                    {
                        request.Completion.TrySetResult(result);
                    }
                }
            }
            finally
            {
                Release(automation);
                if (initialized >= 0) CoUninitialize();
            }
        }
    }
}
