using System.Runtime.InteropServices;

namespace RitsukageGif.Core
{
    internal sealed unsafe class OnnxRuntimeLibrary : IDisposable
    {
        internal readonly nint Allocator;
        internal readonly nint* Api;
        internal readonly nint Environment;
        internal readonly nint MemoryInfo;
        private readonly nint _module;

        public OnnxRuntimeLibrary(string path)
        {
            _module = NativeLibrary.Load(Path.GetFullPath(path));
            var getBase = (delegate* unmanaged<nint>)NativeLibrary.GetExport(_module, "OrtGetApiBase");
            var apiBase = (nint*)getBase();
            Api = (nint*)((delegate* unmanaged<uint, nint>)apiBase[0])(23);
            if (Api is null)
            {
                NativeLibrary.Free(_module);
                throw new NotSupportedException("The inference runtime does not provide C API version 23.");
            }

            nint environment = 0, allocator = 0, memory = 0;
            try
            {
                fixed (byte* logName = "RitsukageGif.Recognition\0"u8)
                    Check(((delegate* unmanaged<int, byte*, nint*, nint>)Api[(int)OnnxApi.CreateEnv])(3, logName,
                        &environment));
                Check(((delegate* unmanaged<nint, nint>)Api[(int)OnnxApi.DisableTelemetryEvents])(environment));
                Check(((delegate* unmanaged<nint*, nint>)Api[(int)OnnxApi.GetAllocatorWithDefaultOptions])(&allocator));
                Check(
                    ((delegate* unmanaged<int, int, nint*, nint>)Api[(int)OnnxApi.CreateCpuMemoryInfo])(1, 0, &memory));
                Environment = environment;
                Allocator = allocator;
                MemoryInfo = memory;
            }
            catch
            {
                Release(OnnxApi.ReleaseMemoryInfo, memory);
                Release(OnnxApi.ReleaseEnv, environment);
                NativeLibrary.Free(_module);
                throw;
            }
        }

        public void Dispose()
        {
            Release(OnnxApi.ReleaseMemoryInfo, MemoryInfo);
            Release(OnnxApi.ReleaseEnv, Environment);
            NativeLibrary.Free(_module);
        }

        internal void Check(nint status)
        {
            if (status == 0) return;
            var message =
                Marshal.PtrToStringUTF8(((delegate* unmanaged<nint, nint>)Api[(int)OnnxApi.GetErrorMessage])(status));
            Release(OnnxApi.ReleaseStatus, status);
            throw new InvalidOperationException(message);
        }

        internal void Release(OnnxApi function, nint handle)
        {
            if (handle != 0) ((delegate* unmanaged<nint, void>)Api[(int)function])(handle);
        }

        internal string AllocatedString(nint value)
        {
            try
            {
                return Marshal.PtrToStringUTF8(value) ?? "";
            }
            finally
            {
                Check(((delegate* unmanaged<nint, nint, nint>)Api[(int)OnnxApi.AllocatorFree])(Allocator, value));
            }
        }
    }
}
