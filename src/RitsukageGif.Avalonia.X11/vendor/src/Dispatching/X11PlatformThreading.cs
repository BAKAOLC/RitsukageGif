using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;
using Avalonia.X11.Dispatching;

namespace Avalonia.X11
{
    internal unsafe class X11PlatformThreading : IControlledDispatcherImpl, IX11PlatformDispatcher
    {
        private readonly AvaloniaX11Platform _platform;
        private readonly Thread _mainThread = Thread.CurrentThread;
        private const int EPOLLIN = 1;
        private const int EPOLL_CTL_ADD = 1;
        private const int O_NONBLOCK = 2048;
        private readonly object _lock = new();
        private bool _signaled;
        private bool _wakeupRequested;
        private long? _nextTimer;
        private readonly int _epoll;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public bool CurrentThreadIsLoopThread => Thread.CurrentThread == _mainThread;


        public long Now => _clock.ElapsedMilliseconds;

        public X11PlatformThreading(AvaloniaX11Platform platform)
        {
            _platform = platform;
            EventDispatcher = new(platform);
            var ev = new epoll_event
            {
                events = EPOLLIN,
                data = { u32 = (int)EventCodes.X11 },
            };
            _epoll = epoll_create1(0);
            if (_epoll == -1)
                throw new X11Exception("epoll_create1 failed");

            if (epoll_ctl(_epoll, EPOLL_CTL_ADD, EventDispatcher.Fd, ref ev) == -1)
                throw new X11Exception("Unable to attach X11 connection handle to epoll");

            var fds = stackalloc int[2];
            pipe2(fds, O_NONBLOCK);
            _sigread = fds[0];
            _sigwrite = fds[1];

            ev = new()
            {
                events = EPOLLIN,
                data = { u32 = (int)EventCodes.Signal },
            };
            if (epoll_ctl(_epoll, EPOLL_CTL_ADD, _sigread, ref ev) == -1)
                throw new X11Exception("Unable to attach signal pipe to epoll");
        }

        public bool CanQueryPendingInput => true;

        public bool HasPendingInput => _platform.EventGrouperDispatchQueue.HasJobs || EventDispatcher.IsPending;


        public void RunLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _clock.ElapsedMilliseconds;
                if (_nextTimer.HasValue && now > _nextTimer.Value) Timer?.Invoke();

                if (cancellationToken.IsCancellationRequested)
                    return;

                //Flush whatever requests were made to XServer
                EventDispatcher.Flush();
                epoll_event ev;
                if (!EventDispatcher.IsPending)
                {
                    now = _clock.ElapsedMilliseconds;
                    if (_nextTimer < now)
                        continue;

                    var timeout = _nextTimer == null ? -1 : Math.Max(1, _nextTimer.Value - now);
                    epoll_wait(_epoll, &ev, 1, (int)Math.Min(int.MaxValue, timeout));

                    // Drain the signaled pipe
                    var buf = 0;
                    while (read(_sigread, &buf, new(4)).ToInt64() > 0)
                    {
                    }

                    lock (_lock)
                        _wakeupRequested = false;
                }

                if (cancellationToken.IsCancellationRequested)
                    return;
                CheckSignaled();
                EventDispatcher.DispatchX11Events(cancellationToken);
                while (_platform.EventGrouperDispatchQueue.HasJobs)
                {
                    CheckSignaled();
                    _platform.EventGrouperDispatchQueue.DispatchNext();
                }
            }
        }

        public void Signal()
        {
            lock (_lock)
            {
                if (_signaled)
                    return;
                _signaled = true;
                Wakeup();
            }
        }
        public X11EventDispatcher EventDispatcher { get; }

        public event Action? Signaled;
        public event Action? Timer;

        public void UpdateTimer(long? dueTimeInMs)
        {
            _nextTimer = dueTimeInMs;
            if (_nextTimer != null)
                Wakeup();
        }

        private readonly int _sigread;
        private readonly int _sigwrite;

        [DllImport("libc")]
        private static extern int epoll_create1(int size);

        [DllImport("libc")]
        private static extern int epoll_ctl(int epfd, int op, int fd, ref epoll_event __event);

        [DllImport("libc")]
        private static extern int epoll_wait(int epfd, epoll_event* events, int maxevents, int timeout);

        [DllImport("libc")]
        private static extern int pipe2(int* fds, int flags);

        [DllImport("libc")]
        private static extern IntPtr write(int fd, void* buf, IntPtr count);

        [DllImport("libc")]
        private static extern IntPtr read(int fd, void* buf, IntPtr count);

        private void CheckSignaled()
        {
            lock (_lock)
            {
                if (!_signaled)
                    return;
                _signaled = false;
            }

            Signaled?.Invoke();
        }

        private void Wakeup()
        {
            lock (_lock)
            {
                if (_wakeupRequested)
                    return;
                _wakeupRequested = true;
                var buf = 0;
                write(_sigwrite, &buf, new(1));
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct epoll_data
        {
            [FieldOffset(0)] public IntPtr ptr;
            [FieldOffset(0)] public int fd;
            [FieldOffset(0)] public uint u32;
            [FieldOffset(0)] public ulong u64;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct epoll_event
        {
            public uint events;
            public epoll_data data;
        }

        private enum EventCodes
        {
            X11 = 1,
            Signal = 2,
        }
    }
}
