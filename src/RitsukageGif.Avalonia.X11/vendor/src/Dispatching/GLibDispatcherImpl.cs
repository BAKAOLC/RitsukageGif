using System;
using System.Threading;
using static Avalonia.X11.Interop.Glib;

namespace Avalonia.X11.Dispatching
{
    internal class GlibDispatcherImpl : GlibDispatcherImplBase, IX11PlatformDispatcher
    {
        private readonly AvaloniaX11Platform _platform;

        public GlibDispatcherImpl(AvaloniaX11Platform platform)
            : base(platform.Options.ExternalGLibMainLoopExceptionLogger)
        {
            _platform = platform;
            EventDispatcher = new(platform);
            var unixFdId = g_unix_fd_add_full(G_PRIORITY_DEFAULT, EventDispatcher.Fd, GIOCondition.G_IO_IN,
                X11SourceCallback);
            // We can trigger a nested event loop when handling X11 events, so we need to mark the source as recursive
            var unixFdSource = g_main_context_find_source_by_id(IntPtr.Zero, unixFdId);
            g_source_set_can_recurse(unixFdSource, 1);
        }

        public override bool HasPendingInput =>
            _platform.EventGrouperDispatchQueue.HasJobs || EventDispatcher.IsPending;

        public X11EventDispatcher EventDispatcher { get; }

        protected override void Flush()
        {
            EventDispatcher.Flush();
        }

        private bool X11SourceCallback(int i, GIOCondition gioCondition)
        {
            CheckSignaled();
            var token = CurrentLoopCancellation;
            try
            {
                // Completely drain X11 socket while we are at it
                while (EventDispatcher.IsPending)
                {
                    // If we don't actually drain our X11 socket, GLib _will_ call us again even if
                    // we request the run loop to quit
                    EventDispatcher.DispatchX11Events(CancellationToken.None);
                    if (!token.IsCancellationRequested)
                    {
                        while (_platform.EventGrouperDispatchQueue.HasJobs)
                        {
                            CheckSignaled();
                            _platform.EventGrouperDispatchQueue.DispatchNext();
                        }

                        EventDispatcher.Flush();
                    }
                }
            }
            catch (Exception e)
            {
                HandleException(e);
            }

            return true;
        }
    }
}
