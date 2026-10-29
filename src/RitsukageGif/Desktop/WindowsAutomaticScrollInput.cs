using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class WindowsAutomaticScrollInput : IAutomaticScrollInput
    {
        private readonly PixelRect _client;
        private readonly nint _control;
        private readonly uint _process;
        private readonly WindowTarget _target;

        private WindowsAutomaticScrollInput(WindowTarget target, nint control, uint process)
        {
            _target = target;
            _client = target.Client;
            _control = control;
            _process = process;
        }

        public AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() || WindowLocator.IsWindow(_target.Handle) == 0 ||
                NativeMethods.IsWindowVisible(_target.Handle) == 0 || NativeMethods.IsIconic(_target.Handle) != 0 ||
                GetWindowThreadProcessId(_target.Handle, out var process) == 0 || process != _process ||
                GetWindowThreadProcessId(_control, out var controlProcess) == 0 || controlProcess != _process ||
                NativeMethods.IsWindowVisible(_control) == 0 ||
                (_control != _target.Handle && IsChild(_target.Handle, _control) == 0))
                return AutomaticScrollInputResult.Unavailable;
            var current = WindowLocator.Refresh(_target);
            if (current is null || current.Client != _client || region.Intersect(current.Client) != region)
                return AutomaticScrollInputResult.Unavailable;
            var bar = axis == ScrollCaptureAxis.Vertical ? 1 : 0;
            var info = new WindowsAutomaticScrollInfo
                { Size = (uint)Marshal.SizeOf<WindowsAutomaticScrollInfo>(), Mask = 7 };
            if (GetScrollInfo(_control, bar, ref info) != 0 && info.Maximum > info.Minimum && info.Page > 0 &&
                info.Position >= info.Maximum - info.Page + 1)
                return AutomaticScrollInputResult.AtEnd;
            var x = region.X + region.Width / 2;
            var y = region.Y + region.Height / 2;
            if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)
                return AutomaticScrollInputResult.Unavailable;
            var coordinates = (nint)(((uint)(ushort)y << 16) | (ushort)x);
            var delta = axis == ScrollCaptureAxis.Vertical ? -120 : 120;
            var flags = (nuint)((uint)(ushort)delta << 16);
            var message = axis == ScrollCaptureAxis.Vertical ? 0x020Au : 0x020Eu;
            token.ThrowIfCancellationRequested();
            return SendMessageTimeout(_control, message, flags, coordinates, 3, 100, out _) != 0
                ? AutomaticScrollInputResult.Sent
                : AutomaticScrollInputResult.Failed;
        }

        internal static IAutomaticScrollInput? Create(PixelRect region, WindowTarget? source)
        {
            if (!OperatingSystem.IsWindows() || region.IsEmpty) return null;
            var point = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
            var target = source ?? WindowLocator.List().FirstOrDefault(window => window.Bounds.Contains(point));
            if (target is null || region.Intersect(target.Client) != region ||
                GetWindowThreadProcessId(target.Handle, out var process) == 0) return null;
            var child = WindowControlLocator.Find(target, point)?.Handle ?? target.Handle;
            return new WindowsAutomaticScrollInput(target, child, process);
        }

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);

        [LibraryImport("user32.dll")]
        private static partial int IsChild(nint parent, nint child);

        [LibraryImport("user32.dll")]
        private static partial int GetScrollInfo(nint window, int bar, ref WindowsAutomaticScrollInfo info);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
        private static partial nint SendMessageTimeout(nint window, uint message, nuint flags, nint coordinates,
            uint options, uint timeout, out nuint result);
    }
}
