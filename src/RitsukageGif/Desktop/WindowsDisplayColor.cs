using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsDisplayColor
    {
        private static readonly Lock Sync = new();
        private static readonly Dictionary<nint, (bool Hdr, float WhiteLevel)> Cache = [];

        public static void Invalidate()
        {
            lock (Sync) Cache.Clear();
        }

        public static (bool Hdr, float WhiteLevel) Read(nint monitor)
        {
            lock (Sync)
            {
                if (Cache.TryGetValue(monitor, out var state)) return state;
                state = Query(monitor);
                Cache.Add(monitor, state);
                return state;
            }
        }

        private static (bool Hdr, float WhiteLevel) Query(nint monitor)
        {
            var info = new MonitorInfo { Size = 104 };
            if (GetMonitorInfo(monitor, ref info) == 0 ||
                GetDisplayConfigBufferSizes(2, out var paths, out var modes) != 0 || paths > 128 || modes > 1024)
                return (false, 1);
            var pathBytes = new byte[checked((int)paths * 72)];
            var modeBytes = new byte[checked((int)modes * 64)];
            fixed (byte* path = pathBytes, mode = modeBytes)
            {
                if (QueryDisplayConfig(2, ref paths, path, ref modes, mode, 0) != 0) return (false, 1);
                for (var index = 0; index < paths; index++)
                {
                    var entry = path + index * 72;
                    var source = new SourceName
                        { Type = 1, Size = 84, Adapter = *(long*)entry, Id = *(uint*)(entry + 8) };
                    if (DisplayConfigGetDeviceInfo(&source) != 0 ||
                        new string(source.Name) != new string(info.Device)) continue;
                    var color = new ColorInfo
                        { Type = 9, Size = 32, Adapter = *(long*)(entry + 20), Id = *(uint*)(entry + 28) };
                    if (DisplayConfigGetDeviceInfo(&color) != 0 || (color.Value & 2) == 0) return (false, 1);
                    var advanced = new ColorInfo2 { Type = 15, Size = 36, Adapter = color.Adapter, Id = color.Id };
                    if (DisplayConfigGetDeviceInfo(&advanced) == 0 && advanced.Mode != 2) return (false, 1);
                    var white = new WhiteInfo { Type = 11, Size = 24, Adapter = color.Adapter, Id = color.Id };
                    var level = DisplayConfigGetDeviceInfo(&white) == 0 ? white.Value / 1000f : 1;
                    return (true, Math.Clamp(level, 1, 125));
                }
            }

            return (false, 1);
        }

        public static PixelRect Bounds(nint monitor)
        {
            var info = new MonitorInfo { Size = 104 };
            if (GetMonitorInfo(monitor, ref info) == 0) throw new CaptureSourceLostException();
            return new(info.Rectangles[0], info.Rectangles[1], info.Rectangles[2] - info.Rectangles[0],
                info.Rectangles[3] - info.Rectangles[1]);
        }

        public static nint ForWindow(nint window)
        {
            return MonitorFromWindow(window, 2);
        }

        public static nint ForRegion(PixelRect region)
        {
            return MonitorFromPoint(new() { X = region.X + region.Width / 2, Y = region.Y + region.Height / 2 }, 2);
        }

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        private static partial int GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [LibraryImport("user32.dll")]
        private static partial int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

        [LibraryImport("user32.dll")]
        private static partial int QueryDisplayConfig(uint flags, ref uint pathCount, void* paths, ref uint modeCount,
            void* modes, nint topology);

        [LibraryImport("user32.dll")]
        private static partial int DisplayConfigGetDeviceInfo(void* packet);

        [LibraryImport("user32.dll")]
        private static partial nint MonitorFromWindow(nint window, uint flags);

        [LibraryImport("user32.dll")]
        private static partial nint MonitorFromPoint(NativePoint point, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public uint Size;
            public fixed int Rectangles[8];
            public uint Flags;
            public fixed char Device[32];
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SourceName
        {
            public uint Type, Size;
            public long Adapter;
            public uint Id;
            public fixed char Name[32];
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ColorInfo
        {
            public uint Type, Size;
            public long Adapter;
            public uint Id, Value, Encoding, Bits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ColorInfo2
        {
            public uint Type, Size;
            public long Adapter;
            public uint Id, Value, Encoding, Bits, Mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WhiteInfo
        {
            public uint Type, Size;
            public long Adapter;
            public uint Id, Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X, Y;
        }
    }
}
