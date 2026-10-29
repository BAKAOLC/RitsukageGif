using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Core
{
    internal sealed partial class OutputFileLease : IDisposable
    {
        private readonly string _stage;
        private readonly FileStream _stream;
        private string? _destination;

        internal OutputFileLease(string stage)
        {
            _stage = stage;
            if (!OperatingSystem.IsWindows())
            {
                _stream = new(stage, FileMode.Open, FileAccess.Read, FileShare.Read);
                return;
            }

            var handle = CreateFile(Path.GetFullPath(stage), 0x80010000, 1, 0, 3, 0x80, 0);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException(stage, new Win32Exception(error));
            }

            try
            {
                _stream = new(handle, FileAccess.Read);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        internal unsafe void Publish(string destination)
        {
            if (OperatingSystem.IsWindows())
            {
                var path = Path.GetFullPath(destination);
                var nameOffset = IntPtr.Size == 8 ? 20 : 12;
                var data = new byte[nameOffset + Encoding.Unicode.GetByteCount(path)];
                BitConverter.TryWriteBytes(data.AsSpan(nameOffset - 4, 4), data.Length - nameOffset);
                Encoding.Unicode.GetBytes(path.AsSpan(), data.AsSpan(nameOffset));
                fixed (byte* buffer = data)
                    if (!SetFileInformation(_stream.SafeFileHandle, 3, buffer, (uint)data.Length))
                        throw new IOException(destination, new Win32Exception(Marshal.GetLastPInvokeError()));
            }
            else
            {
                File.Move(_stage, destination);
            }

            _destination = destination;
        }

        internal unsafe void Rollback()
        {
            if (_destination is null) return;
            if (OperatingSystem.IsWindows())
            {
                var delete = 1;
                if (!SetFileInformation(_stream.SafeFileHandle, 4, &delete, sizeof(int)))
                    throw new IOException(_destination, new Win32Exception(Marshal.GetLastPInvokeError()));
                return;
            }

            // POSIX does not enforce Windows share-delete leases. Refuse to remove different output data.
            using var current = new FileStream(_destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            _stream.Position = 0;
            if (!SHA256.HashData(current).AsSpan().SequenceEqual(SHA256.HashData(_stream)))
                throw new IOException(_destination);
            File.Delete(_destination);
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16)]
        private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security,
            uint creation, uint flags, nint template);

        [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool SetFileInformation(SafeFileHandle handle, int informationClass,
            void* information, uint size);
    }
}
