using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsAudioCaptureDevice : IDisposable
    {
        private nint _capture;
        private nint _client;
        private nint _device;
        private nint _enumerator;
        private bool _initialized;
        private bool _started;

        public WindowsAudioCaptureDevice(bool loopback, string? deviceId, EventWaitHandle signal)
        {
            try
            {
                Check(CoInitializeEx(0, 0));
                _initialized = true;
                var classId = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
                var enumeratorId = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
                nint enumerator;
                Check(CoCreateInstance(&classId, 0, 1, &enumeratorId, &enumerator));
                _enumerator = enumerator;
                nint device;
                if (string.IsNullOrEmpty(deviceId))
                    Check(((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Table(_enumerator)[4])(
                        _enumerator, loopback ? 0 : 1, 0, &device));
                else
                    fixed (char* id = deviceId)
                        Check(((delegate* unmanaged[Stdcall]<nint, char*, nint*, int>)Table(_enumerator)[5])(
                            _enumerator, id, &device));
                _device = device;
                var clientId = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
                nint client;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Table(_device)[3])(
                    _device, &clientId, 1, 0, &client));
                _client = client;
                nint format;
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(_client)[8])(_client, &format));
                try
                {
                    Format = ReadFormat(new((void*)format, 18 + *(ushort*)(format + 16)));
                    var flags = 0x00040000u | (loopback ? 0x00020000u : 0u);
                    Check(
                        ((delegate* unmanaged[Stdcall]<nint, int, uint, long, long, nint, nint, int>)Table(_client)[3])(
                            _client, 0, flags, 1_000_000, 0, format, 0));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(format);
                }

                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Table(_client)[13])(
                    _client, signal.SafeWaitHandle.DangerousGetHandle()));
                var captureId = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
                nint capture;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(_client)[14])(_client, &captureId,
                    &capture));
                _capture = capture;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public AudioCaptureFormat Format { get; }

        public void Dispose()
        {
            if (_started)
            {
                _ = ((delegate* unmanaged[Stdcall]<nint, int>)Table(_client)[11])(_client);
                _started = false;
            }

            Release(ref _capture);
            Release(ref _client);
            Release(ref _device);
            Release(ref _enumerator);
            if (!_initialized) return;
            CoUninitialize();
            _initialized = false;
        }

        public void Start()
        {
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Table(_client)[10])(_client));
            _started = true;
        }

        public bool Read(Action<nint, uint, uint, ulong> consume)
        {
            uint next;
            Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(_capture)[5])(_capture, &next));
            if (next == 0) return false;
            nint data = 0;
            uint frames = 0, flags = 0;
            ulong position = 0, timestamp = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, uint*, uint*, ulong*, ulong*, int>)Table(_capture)[3])(
                _capture, &data, &frames, &flags, &position, &timestamp));
            if (frames == 0) return false;
            try
            {
                consume(data, frames, flags, timestamp);
            }
            finally
            {
                Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Table(_capture)[4])(_capture, frames));
            }

            return true;
        }

        private static AudioCaptureFormat ReadFormat(ReadOnlySpan<byte> data)
        {
            if (data.Length < 18) throw new NotSupportedException("Invalid audio format.");
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(data);
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            var align = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
            uint channelMask = 0;
            if (tag == 0xFFFE)
            {
                if (data.Length < 40) throw new NotSupportedException("Invalid extensible audio format.");
                channelMask = BinaryPrimitives.ReadUInt32LittleEndian(data[20..]);
                var subtype = new Guid(data.Slice(24, 16));
                tag = subtype == new Guid("00000001-0000-0010-8000-00AA00389B71") ? (ushort)1 :
                    subtype == new Guid("00000003-0000-0010-8000-00AA00389B71") ? (ushort)3 : (ushort)0;
            }

            if (tag is not (1 or 3) || align != channels * bits / 8)
                throw new NotSupportedException("Unsupported audio mix format.");
            var result = new AudioCaptureFormat(sampleRate, channels, bits, tag == 3, channelMask);
            result.Validate();
            return result;
        }

        private static void Check(int result)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        private static nint* Table(nint instance)
        {
            return *(nint**)instance;
        }

        private static void Release(ref nint instance)
        {
            if (instance == 0) return;
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)Table(instance)[2])(instance);
            instance = 0;
        }

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(nint reserved, uint flags);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("ole32.dll")]
        private static partial int CoCreateInstance(Guid* classId, nint outer, uint context, Guid* interfaceId,
            nint* instance);
    }
}
