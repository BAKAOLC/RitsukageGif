namespace RitsukageGif.Desktop
{
    internal sealed partial record ForegroundHotkeyContext
    {
        internal static unsafe string? ReadProgram(nint window)
        {
            if (GetWindowThreadProcessId(window, out var processId) == 0) return null;
            using var process = OpenProcess(0x1000, 0, processId);
            if (process.IsInvalid) return null;
            Span<char> buffer = stackalloc char[32768];
            var length = (uint)buffer.Length;
            fixed (char* data = buffer)
                return QueryFullProcessImageName(process, 0, data, ref length) != 0
                    ? new string(data, 0, (int)length)
                    : null;
        }
    }
}
