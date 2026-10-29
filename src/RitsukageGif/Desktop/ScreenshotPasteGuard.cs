using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class ScreenshotPasteGuard
    {
        internal static uint ClipboardSequence => OperatingSystem.IsWindows() ? GetClipboardSequenceNumber() : 0;

        internal static async Task<ScreenshotPasteTarget?> CaptureTargetAsync(CancellationToken token)
        {
            if (!OperatingSystem.IsWindows()) return null;
            var window = GetForegroundWindow();
            var thread = GetWindowThreadProcessId(window, out var process);
            if (window == 0 || thread == 0 || process == Environment.ProcessId) return null;
            var info = new PasteFocusWindowInfo { Size = (uint)Marshal.SizeOf<PasteFocusWindowInfo>() };
            if (!GetGuiThreadInfo(thread, ref info) || info.Active != window || info.Focus == 0) return null;
            var identity = await WindowsAccessibilityLocator.ReadFocusedIdentityAsync(window, token);
            if (identity is null) return null;
            var target = new ScreenshotPasteTarget(window, info.Focus, process, thread, identity);
            return MatchesWindow(target) ? target : null;
        }

        internal static async Task TryPasteAsync(ScreenshotPasteTarget? target, uint clipboardSequence,
            CancellationToken token)
        {
            if (!OperatingSystem.IsWindows() || target is null || clipboardSequence == 0) return;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (!MatchesWindow(target) || ClipboardSequence != clipboardSequence) return;
                if (ModifiersReleased()) break;
                if (attempt == 19) return;
                await Task.Delay(15, token);
            }

            var identity = await WindowsAccessibilityLocator.ReadFocusedIdentityAsync(target.Window, token);
            if (!target.Identity.Matches(identity) || !MatchesWindow(target) || !ModifiersReleased() ||
                ClipboardSequence != clipboardSequence) return;
            token.ThrowIfCancellationRequested();
            var inputs = new[] { Key(0x11, 0), Key(0x56, 0), Key(0x56, 2), Key(0x11, 2) };
            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<PasteNativeInput>());
            if (sent == inputs.Length) return;
            if (sent > 0)
            {
                var release = sent >= 2 ? new[] { Key(0x56, 2), Key(0x11, 2) } : new[] { Key(0x11, 2) };
                if (SendInput((uint)release.Length, release, Marshal.SizeOf<PasteNativeInput>()) != release.Length)
                    throw new InvalidOperationException("Unable to release the injected paste keys.");
            }
        }

        private static bool MatchesWindow(ScreenshotPasteTarget target)
        {
            if (GetForegroundWindow() != target.Window || !IsWindow(target.Focus) ||
                GetWindowThreadProcessId(target.Window, out var process) != target.ThreadId ||
                process != target.ProcessId)
                return false;
            var info = new PasteFocusWindowInfo { Size = (uint)Marshal.SizeOf<PasteFocusWindowInfo>() };
            return GetGuiThreadInfo(target.ThreadId, ref info) && info.Active == target.Window &&
                   info.Focus == target.Focus;
        }

        private static bool ModifiersReleased()
        {
            return (GetAsyncKeyState(0x10) & 0x8000) == 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                   (GetAsyncKeyState(0x12) & 0x8000) == 0 && (GetAsyncKeyState(0x5b) & 0x8000) == 0 &&
                   (GetAsyncKeyState(0x5c) & 0x8000) == 0;
        }

        private static PasteNativeInput Key(ushort key, uint flags)
        {
            return new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = flags } } };
        }

        [LibraryImport("user32.dll")]
        private static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint window, out uint process);

        [LibraryImport("user32.dll")]
        private static partial uint GetClipboardSequenceNumber();

        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int key);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsWindow(nint window);

        [LibraryImport("user32.dll", EntryPoint = "GetGUIThreadInfo")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetGuiThreadInfo(uint thread, ref PasteFocusWindowInfo info);

        [LibraryImport("user32.dll")]
        private static partial uint SendInput(uint count, [In] PasteNativeInput[] inputs, int size);
    }
}
