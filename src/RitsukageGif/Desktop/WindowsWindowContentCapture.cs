using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    // Direct WinRT/D3D11 ABI calls keep window capture usable in Native AOT without runtime COM wrappers.
    internal sealed unsafe partial class WindowsWindowContentCapture : IDisposable
    {
        private nint _device, _context, _winrtDevice, _item, _pool, _session, _staging, _mta;
        private Size _size;
        private TextureDescription _description;
        private byte[]? _pixels;
        private readonly nint _window;
        private readonly CaptureFrameSignal _signal = new();
        private nint _frameHandler, _closedHandler;
        private long _frameToken, _closedToken;
        private bool _frameSubscribed, _closedSubscribed, _disposed;
        public int Width => _size.Width;
        public int Height => _size.Height;

        public WindowsWindowContentCapture(nint window, bool cursor)
        {
            _window = window;
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
                    throw new NotSupportedException(UiText.Get("WindowCaptureUnsupported"));
                Check(CoIncrementMtaUsage(out _mta));
                Check(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out _device, out _, out _context));
                var dxgi = Query(_device, new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"));
                try
                {
                    Check(CreateDirect3D11DeviceFromDxgiDevice(dxgi, out _winrtDevice));
                }
                finally
                {
                    Release(ref dxgi);
                }

                var factory = Activate("Windows.Graphics.Capture.GraphicsCaptureItem",
                    new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"));
                var iid = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
                nint item = 0;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Vtable(factory)[3])(factory,
                        window,
                        &iid, &item));
                }
                finally
                {
                    Release(ref factory);
                }

                _item = item;
                Size size;
                Check(((delegate* unmanaged[Stdcall]<nint, Size*, int>)Vtable(_item)[7])(_item, &size));
                ValidateSize(size);
                _size = size;
                factory = Activate("Windows.Graphics.Capture.Direct3D11CaptureFramePool",
                    new Guid("589b103f-6bbc-5df5-a991-02e28b3b66d5"));
                nint pool = 0;
                try
                {
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Size, nint*, int>)Vtable(factory)[6])(
                        factory, _winrtDevice, 87, 2, size, &pool));
                }
                finally
                {
                    Release(ref factory);
                }

                _pool = pool;
                _frameHandler = _signal.CreateHandler(false);
                long frameToken;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Vtable(_pool)[8])(_pool,
                    _frameHandler, &frameToken));
                _frameToken = frameToken;
                _frameSubscribed = true;
                _closedHandler = _signal.CreateHandler(true);
                long closedToken;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Vtable(_item)[8])(_item,
                    _closedHandler, &closedToken));
                _closedToken = closedToken;
                _closedSubscribed = true;
                nint session = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Vtable(_pool)[10])(_pool, _item,
                    &session));
                _session = session;
                var session2 = TryQuery(session, new Guid("2c39ae40-7d2e-5044-804e-8b6799d4cf9e"));
                if (session2 != 0)
                {
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Vtable(session2)[7])(session2,
                            cursor ? (byte)1 : (byte)0));
                    }
                    finally
                    {
                        Release(ref session2);
                    }
                }

                Check(((delegate* unmanaged[Stdcall]<nint, int>)Vtable(_session)[6])(_session));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public byte[] Capture(CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var deadline = Environment.TickCount64 + 5000;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (_signal.IsClosed || WindowLocator.IsWindow(_window) == 0 || NativeMethods.IsIconic(_window) != 0)
                    throw new CaptureSourceLostException();
                var pixels = TryCapture();
                if (pixels is not null) return pixels;
                _signal.Wait(deadline, token);
            }
        }

        private byte[]? TryCapture()
        {
            nint frame = 0;
            // Drain old frames so a slow GIF encoder cannot make window capture lag behind the scene.
            for (var i = 0; i < 3; i++)
            {
                nint next = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(_pool)[7])(_pool, &next));
                if (next == 0) break;
                Close(ref frame);
                frame = next;
            }

            if (frame == 0) return _pixels;
            nint surface = 0, access = 0, texture = 0;
            try
            {
                Size size;
                Check(((delegate* unmanaged[Stdcall]<nint, Size*, int>)Vtable(frame)[8])(frame, &size));
                if (size.Width <= 0 || size.Height <= 0) throw new CaptureSourceLostException();
                ValidateSize(size);
                if (size.Width != _size.Width || size.Height != _size.Height)
                {
                    Close(ref frame);
                    _pixels = null;
                    _size = size;
                    Release(ref _staging);
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Size, int>)Vtable(_pool)[6])(_pool,
                        _winrtDevice, 87, 2, size));
                    return null;
                }

                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtable(frame)[6])(frame, &surface));
                access = Query(surface, new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1"));
                var textureId = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtable(access)[3])(access, &textureId,
                    &texture));
                TextureDescription description;
                ((delegate* unmanaged[Stdcall]<nint, TextureDescription*, void>)Vtable(texture)[10])(texture,
                    &description);
                if (_staging == 0 || description.Width != _description.Width ||
                    description.Height != _description.Height)
                {
                    Release(ref _staging);
                    _description = description;
                    description.Usage = 3;
                    description.BindFlags = 0;
                    description.CpuAccess = 0x20000;
                    description.MiscFlags = 0;
                    nint staging;
                    Check(
                        ((delegate* unmanaged[Stdcall]<nint, TextureDescription*, nint, nint*, int>)Vtable(_device)[5])(
                            _device, &description, 0, &staging));
                    _staging = staging;
                }

                ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Vtable(_context)[47])(_context, _staging,
                    texture);
                MappedResource map;
                Check(
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, MappedResource*, int>)Vtable(
                        _context)[14])(_context, _staging, 0, 1, 0, &map));
                try
                {
                    _pixels ??= new byte[checked(size.Width * size.Height * 4)];
                    for (var y = 0; y < size.Height; y++)
                        new ReadOnlySpan<byte>((byte*)map.Data + y * map.RowPitch, size.Width * 4).CopyTo(
                            _pixels.AsSpan(y * size.Width * 4));
                }
                finally
                {
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Vtable(_context)[15])(_context, _staging, 0);
                }

                return _pixels;
            }
            finally
            {
                Release(ref texture);
                Release(ref access);
                Release(ref surface);
                Close(ref frame);
            }
        }

        private static void ValidateSize(Size size)
        {
            if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > 64 * 1024 * 1024)
                throw new InvalidOperationException(UiText.Get("RecordingTooLarge"));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_frameSubscribed)
                ((delegate* unmanaged[Stdcall]<nint, long, int>)Vtable(_pool)[9])(_pool, _frameToken);
            if (_closedSubscribed)
                ((delegate* unmanaged[Stdcall]<nint, long, int>)Vtable(_item)[9])(_item, _closedToken);
            Close(ref _session);
            Close(ref _pool);
            Release(ref _frameHandler);
            Release(ref _closedHandler);
            _signal.Dispose();
            Release(ref _staging);
            Release(ref _item);
            Release(ref _winrtDevice);
            Release(ref _context);
            Release(ref _device);
            if (_mta != 0)
            {
                Check(CoDecrementMtaUsage(_mta));
                _mta = 0;
            }
        }

        private static nint* Vtable(nint instance) => *(nint**)instance;

        private static void Check(int result)
        {
            if (result < 0) Marshal.ThrowExceptionForHR(result);
        }

        private static nint Activate(string name, Guid iid)
        {
            Check(WindowsCreateString(name, name.Length, out var text));
            try
            {
                Check(RoGetActivationFactory(text, in iid, out var result));
                return result;
            }
            finally
            {
                Check(WindowsDeleteString(text));
            }
        }

        private static nint TryQuery(nint instance, Guid iid)
        {
            nint result = 0;
            var hr = ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtable(instance)[0])(instance, &iid,
                &result);
            return hr < 0 ? 0 : result;
        }

        private static nint Query(nint instance, Guid iid) => TryQuery(instance, iid) is var result && result != 0
            ? result
            : throw new NotSupportedException(UiText.Get("WindowCaptureUnsupported"));

        private static void Release(ref nint instance)
        {
            if (instance == 0) return;
            ((delegate* unmanaged[Stdcall]<nint, uint>)Vtable(instance)[2])(instance);
            instance = 0;
        }

        private static void Close(ref nint instance)
        {
            if (instance == 0) return;
            var closable = TryQuery(instance, new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e"));
            if (closable != 0)
            {
                ((delegate* unmanaged[Stdcall]<nint, int>)Vtable(closable)[6])(closable);
                Release(ref closable);
            }

            Release(ref instance);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Size
        {
            public int Width, Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDescription
        {
            public uint Width,
                Height,
                MipLevels,
                ArraySize,
                Format,
                SampleCount,
                SampleQuality,
                Usage,
                BindFlags,
                CpuAccess,
                MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MappedResource
        {
            public nint Data;
            public uint RowPitch, DepthPitch;
        }

        [LibraryImport("ole32.dll", EntryPoint = "CoIncrementMTAUsage")]
        private static partial int CoIncrementMtaUsage(out nint cookie);

        [LibraryImport("ole32.dll", EntryPoint = "CoDecrementMTAUsage")]
        private static partial int CoDecrementMtaUsage(nint cookie);

        [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int WindowsCreateString(string text, int length, out nint value);

        [LibraryImport("combase.dll")]
        private static partial int WindowsDeleteString(nint text);

        [LibraryImport("combase.dll")]
        private static partial int RoGetActivationFactory(nint name, in Guid iid, out nint result);

        [LibraryImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        private static partial int CreateDirect3D11DeviceFromDxgiDevice(nint device, out nint result);

        [LibraryImport("d3d11.dll")]
        private static partial int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
            nint featureLevels, uint levels, uint sdkVersion, out nint device, out uint level, out nint context);
    }
}
