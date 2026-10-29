using System.Collections.Concurrent;

namespace RitsukageGif.Desktop
{
    internal static partial class WindowsAccessibilityLocator
    {
        private static void RunWorker(BlockingCollection<Request> queue)
        {
            var initialized = CoInitializeEx(0, 0);
            nint automation = 0, walker = 0;
            try
            {
                foreach (var request in queue.GetConsumingEnumerable())
                {
                    IReadOnlyList<WindowControlTarget> result = [];
                    var previousDpi = SetThreadDpiAwarenessContext(-4);
                    try
                    {
                        if (Expired(request)) continue;
                        if (initialized < 0)
                        {
                            initialized = CoInitializeEx(0, 0);
                            if (initialized < 0) continue;
                        }

                        if (automation == 0)
                            if (CoCreateInstance(in AutomationClass, 0, 1, in AutomationInterface, out automation) <
                                0 ||
                                automation == 0 || Configure(automation, 59, 0) < 0 ||
                                Configure(automation, 61, 50) < 0 || Configure(automation, 63, 50) < 0 ||
                                ReadObject(automation, 16, out walker) < 0 || walker == 0)
                            {
                                Release(walker);
                                Release(automation);
                                walker = automation = 0;
                                continue;
                            }

                        if (!Expired(request) && Walk(automation, walker, request) is { } found) result = found;
                    }
                    catch (Exception)
                    {
                        Release(walker);
                        Release(automation);
                        walker = automation = 0;
                    }
                    finally
                    {
                        if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
                        request.Completion.TrySetResult(result);
                    }
                }
            }
            finally
            {
                Release(walker);
                Release(automation);
                if (initialized >= 0) CoUninitialize();
            }
        }

        private static List<WindowControlTarget>? Walk(nint automation, nint walker, Request request)
        {
            if (IsWindowVisible(request.Window.Handle) == 0 ||
                ReadObject(automation, 6, request.Window.Handle, out var current) < 0 || current == 0) return null;
            var result = new List<WindowControlTarget>
                { new(request.Window, request.Window.Handle, request.Window.Bounds) };
            var clip = request.Window.Bounds;
            var visited = 0;
            try
            {
                for (var depth = 0; depth < 64 && !Expired(request); depth++)
                {
                    nint best = 0;
                    var bestBounds = default(PixelRect);
                    var bestArea = long.MaxValue;
                    if (ReadObject(walker, 4, current, out var child) < 0) break;
                    try
                    {
                        while (child != 0 && visited++ < 512 && !Expired(request))
                            if (TryReadBounds(child, clip, out var bounds) && bounds.Contains(request.Point) &&
                                (long)bounds.Width * bounds.Height < bestArea)
                            {
                                Release(best);
                                best = child;
                                bestBounds = bounds;
                                bestArea = (long)bounds.Width * bounds.Height;
                                child = 0;
                                if (ReadObject(walker, 6, best, out child) < 0) break;
                            }
                            else
                            {
                                var previous = child;
                                child = 0;
                                try
                                {
                                    if (ReadObject(walker, 6, previous, out child) < 0) break;
                                }
                                finally
                                {
                                    Release(previous);
                                }
                            }
                    }
                    finally
                    {
                        Release(child);
                    }

                    if (best == 0) break;
                    Release(current);
                    current = best;
                    clip = bestBounds;
                    if (result[^1].Bounds != clip) result.Add(new(request.Window, ReadWindow(current), clip));
                    if (visited >= 512) break;
                }

                return result;
            }
            finally
            {
                Release(current);
            }
        }
    }
}
