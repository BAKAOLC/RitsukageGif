using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Avalonia.Platform;
using Avalonia.Win32.Interop;
using static Avalonia.Win32.Interop.UnmanagedMethods;
using RECT = Windows.Win32.Foundation.RECT;
using Win32Interop = Windows.Win32;

namespace Avalonia.Win32
{
    internal unsafe class ScreenImpl : ScreensBase<nint, WinScreen>
    {
        protected override int GetScreenCount()
        {
            return GetSystemMetrics(SystemMetric.SM_CMONITORS);
        }

        protected override IReadOnlyList<nint> GetAllScreenKeys()
        {
            return GetAllDisplayMonitorHandlers();
        }

        public static List<nint> GetAllDisplayMonitorHandlers()
        {
            var screens = new List<nint>();
            var gcHandle = GCHandle.Alloc(screens);
            try
            {
                Win32Interop.PInvoke.EnumDisplayMonitors(default, default(RECT*), EnumDisplayMonitorsCallback,
                    (IntPtr)gcHandle);
            }
            finally
            {
                gcHandle.Free();
            }

            return screens;

            static BOOL EnumDisplayMonitorsCallback(
                HMONITOR monitor,
                HDC hdcMonitor,
                RECT* lprcMonitor,
                LPARAM dwData)
            {
                if (GCHandle.FromIntPtr(dwData).Target is List<nint> screens)
                {
                    screens.Add(monitor);
                    return true;
                }

                return false;
            }
        }

        protected override WinScreen CreateScreenFromKey(nint key)
        {
            return new(key);
        }

        protected override void ScreenChanged(WinScreen screen)
        {
            screen.Refresh();
        }

        protected override Screen? ScreenFromTopLevelCore(ITopLevelImpl topLevel)
        {
            if (topLevel.Handle?.Handle is { } handle) return ScreenFromHwnd(handle);

            return null;
        }

        protected override Screen? ScreenFromPointCore(PixelPoint point)
        {
            var monitor = MonitorFromPoint(new()
            {
                X = point.X,
                Y = point.Y,
            }, MONITOR.MONITOR_DEFAULTTONULL);

            return ScreenFromHMonitor(monitor);
        }

        protected override Screen? ScreenFromRectCore(PixelRect rect)
        {
            var r = new UnmanagedMethods.RECT
            {
                left = rect.X,
                top = rect.Y,
                right = rect.Right,
                bottom = rect.Bottom,
            };
            var monitor = MonitorFromRect(&r, MONITOR.MONITOR_DEFAULTTONULL);

            return ScreenFromHMonitor(monitor);
        }

        public WinScreen? ScreenFromHMonitor(IntPtr hmonitor)
        {
            if (TryGetScreen(hmonitor, out var screen))
                return screen;

            return null;
        }

        public WinScreen? ScreenFromHwnd(IntPtr hwnd, MONITOR flags = MONITOR.MONITOR_DEFAULTTONULL)
        {
            var monitor = MonitorFromWindow(hwnd, flags);

            return ScreenFromHMonitor(monitor);
        }
    }
}
