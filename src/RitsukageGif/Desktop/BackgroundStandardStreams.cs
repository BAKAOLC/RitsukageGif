using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal static partial class BackgroundStandardStreams
    {
        internal static void DetachIfBackground(string[] args)
        {
            if (args is not (["--serve"] or ["--silent"] or ["--startup"] or ["--task-host", ..])) return;
            Console.SetIn(TextReader.Null);
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            if (OperatingSystem.IsWindows()) DetachWindows();
            else if (OperatingSystem.IsMacOS()) DetachUnix(true);
            else if (OperatingSystem.IsLinux()) DetachUnix(false);
        }

        internal static Process? StartBackground(ProcessStartInfo start)
        {
            if (!OperatingSystem.IsWindows()) return Process.Start(start);
            var handles = new[]
            {
                GetStdHandle(unchecked((uint)-10)), GetStdHandle(unchecked((uint)-11)),
                GetStdHandle(unchecked((uint)-12)),
            };
            var changed = new List<(nint Handle, uint Flags)>();
            Process? process;
            var restoreError = 0;
            try
            {
                for (var index = 0; index < handles.Length; index++)
                {
                    var handle = handles[index];
                    if (handle == 0 || handle == -1 || Array.IndexOf(handles, handle, 0, index) >= 0) continue;
                    if (!GetHandleInformation(handle, out var flags))
                    {
                        var error = Marshal.GetLastPInvokeError();
                        if (GetConsoleMode(handle, out _)) continue;
                        throw new Win32Exception(error);
                    }

                    if ((flags & 1) == 0) continue;
                    if (!SetHandleInformation(handle, 1, 0))
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
                    changed.Add((handle, flags));
                }

                process = Process.Start(start);
            }
            finally
            {
                for (var index = changed.Count - 1; index >= 0; index--)
                    if (!SetHandleInformation(changed[index].Handle, 1, changed[index].Flags & 1) && restoreError == 0)
                        restoreError = Marshal.GetLastPInvokeError();
            }

            if (restoreError != 0)
            {
                process?.Dispose();
                throw new Win32Exception(restoreError);
            }

            return process;
        }

        private static void DetachWindows()
        {
            var input = GetStdHandle(unchecked((uint)-10));
            var output = GetStdHandle(unchecked((uint)-11));
            var error = GetStdHandle(unchecked((uint)-12));
            var sink = CreateFile("NUL", 0xc0000000, 3, 0, 3, 0x80, 0);
            if (sink == -1 || !SetStdHandle(unchecked((uint)-10), sink) ||
                !SetStdHandle(unchecked((uint)-11), sink) || !SetStdHandle(unchecked((uint)-12), sink))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            CloseInherited(input, sink);
            if (output != input) CloseInherited(output, sink);
            if (error != input && error != output) CloseInherited(error, sink);
        }

        private static void CloseInherited(nint handle, nint sink)
        {
            if (handle != 0 && handle != -1 && handle != sink)
                if (!CloseHandle(handle))
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        private static void DetachUnix(bool macOs)
        {
            var sink = macOs ? MacOpen("/dev/null", 2) : LinuxOpen("/dev/null", 2);
            if (sink < 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var closeError = 0;
            try
            {
                for (var descriptor = 0; descriptor < 3; descriptor++)
                    if ((macOs ? MacDup2(sink, descriptor) : LinuxDup2(sink, descriptor)) < 0)
                        throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            finally
            {
                if (sink > 2)
                    if ((macOs ? MacClose(sink) : LinuxClose(sink)) != 0)
                        closeError = Marshal.GetLastPInvokeError();
            }

            if (closeError != 0) throw new Win32Exception(closeError);
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetHandleInformation(nint handle, out uint flags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetConsoleMode(nint handle, out uint mode);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16,
            SetLastError = true)]
        private static partial nint CreateFile(string path, uint access, uint share, nint attributes,
            uint creation, uint flags, nint template);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial nint GetStdHandle(uint kind);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetStdHandle(uint kind, nint handle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);

        [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        private static partial int LinuxOpen(string path, int flags);

        [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
        private static partial int LinuxDup2(int source, int destination);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        private static partial int LinuxClose(int descriptor);

        [LibraryImport("libSystem.B.dylib", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8,
            SetLastError = true)]
        private static partial int MacOpen(string path, int flags);

        [LibraryImport("libSystem.B.dylib", EntryPoint = "dup2", SetLastError = true)]
        private static partial int MacDup2(int source, int destination);

        [LibraryImport("libSystem.B.dylib", EntryPoint = "close", SetLastError = true)]
        private static partial int MacClose(int descriptor);
    }
}
