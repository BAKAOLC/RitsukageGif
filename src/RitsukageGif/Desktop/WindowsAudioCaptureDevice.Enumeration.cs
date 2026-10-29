using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsAudioCaptureDevice
    {
        public static IReadOnlyList<AudioCaptureDevice> Enumerate(bool loopback)
        {
            Check(CoInitializeEx(0, 0));
            nint enumerator = 0, collection = 0;
            try
            {
                var classId = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
                var enumeratorId = new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
                Check(CoCreateInstance(&classId, 0, 1, &enumeratorId, &enumerator));
                var defaultId = DefaultDeviceId(enumerator, loopback);
                Check(((delegate* unmanaged[Stdcall]<nint, int, uint, nint*, int>)Table(enumerator)[3])(
                    enumerator, loopback ? 0 : 1, 1, &collection));
                uint count;
                Check(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(collection)[3])(collection, &count));
                var result = new List<AudioCaptureDevice>();
                for (uint i = 0; i < count; i++)
                {
                    nint device = 0, id = 0, properties = 0;
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(collection)[4])(collection,
                            i, &device));
                        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(device)[5])(device, &id));
                        var identifier = Marshal.PtrToStringUni(id) ??
                                         throw new InvalidDataException("Invalid audio device identifier.");
                        Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table(device)[4])(device, 0,
                            &properties));
                        result.Add(new(identifier, ReadName(properties) ?? identifier,
                            IsDefault: identifier == defaultId));
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(id);
                        Release(ref properties);
                        Release(ref device);
                    }
                }

                return result;
            }
            finally
            {
                Release(ref collection);
                Release(ref enumerator);
                CoUninitialize();
            }
        }

        private static string? DefaultDeviceId(nint enumerator, bool loopback)
        {
            nint device = 0, id = 0;
            try
            {
                var status = ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Table(enumerator)[4])(
                    enumerator, loopback ? 0 : 1, 0, &device);
                if (status == unchecked((int)0x80070490)) return null;
                Check(status);
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(device)[5])(device, &id));
                return Marshal.PtrToStringUni(id);
            }
            finally
            {
                Marshal.FreeCoTaskMem(id);
                Release(ref device);
            }
        }

        private static string? ReadName(nint properties)
        {
            var key = stackalloc byte[20];
            *(Guid*)key = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
            *(uint*)(key + 16) = 14;
            var value = stackalloc byte[24];
            new Span<byte>(value, 24).Clear();
            try
            {
                var result =
                    ((delegate* unmanaged[Stdcall]<nint, byte*, byte*, int>)Table(properties)[5])(properties, key,
                        value);
                return result >= 0 && *(ushort*)value == 31 ? Marshal.PtrToStringUni(*(nint*)(value + 8)) : null;
            }
            finally
            {
                _ = PropVariantClear(value);
            }
        }

        [LibraryImport("ole32.dll")]
        private static partial int PropVariantClear(byte* value);
    }
}
