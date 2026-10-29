using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsHdrPresenter : IDisposable
    {
        private nint _device, _context, _swapChain, _upload;
        private int _width, _height;

        public WindowsHdrPresenter(nint window, int width, int height)
        {
            try
            {
                Check(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out _device, out _, out _context));
                var iid = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
                nint factory = 0;
                Check(CreateDXGIFactory2(0, &iid, &factory));
                try
                {
                    var description = new HdrSwapChainDescription
                    {
                        Width = (uint)width, Height = (uint)height, Format = 10, SampleCount = 1,
                        BufferUsage = 0x20, BufferCount = 2, SwapEffect = 4, AlphaMode = 3,
                    };
                    nint chain = 0;
                    Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, HdrSwapChainDescription*, nint, nint, nint*,
                            int
                            >)
                        Table(factory)[15])(factory, _device, window, &description, 0, 0, &chain));
                    try
                    {
                        _swapChain = Query(chain, new("94d99bdb-f1f8-4ab0-b236-7da0170edab1"));
                    }
                    finally
                    {
                        Release(ref chain);
                    }
                }
                finally
                {
                    Release(ref factory);
                }

                uint support = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, int, uint*, int>)Table(_swapChain)[37])(_swapChain, 1,
                    &support));
                if ((support & 1) == 0) throw new NotSupportedException();
                Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Table(_swapChain)[38])(_swapChain, 1));
                _width = width;
                _height = height;
                CreateUpload();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Release(ref _upload);
            Release(ref _swapChain);
            Release(ref _context);
            Release(ref _device);
        }

        public void Present(ReadOnlySpan<byte> pixels, int width, int height)
        {
            Present(pixels, width, height, checked(width * 8));
        }

        public void Present(ReadOnlySpan<byte> pixels, int width, int height, int stride)
        {
            ObjectDisposedException.ThrowIf(_swapChain == 0, this);
            if (width <= 0 || height <= 0 || stride < (long)width * 8 || pixels.Length < (long)stride * height)
                throw new ArgumentException("Expected a complete FP16 RGBA frame.", nameof(pixels));
            if (_width != width || _height != height)
            {
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, int, uint, int>)Table(_swapChain)[13])(
                    _swapChain, 2, (uint)width, (uint)height, 10, 0));
                _width = width;
                _height = height;
                CreateUpload();
            }

            var iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            nint buffer = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Table(_swapChain)[9])(
                _swapChain, 0, &iid, &buffer));
            try
            {
                fixed (byte* source = pixels)
                    ((delegate* unmanaged[Stdcall]<nint, nint, uint, nint, void*, uint, uint, void>)Table(
                        _context)[48])(
                        _context, _upload, 0, 0, source, (uint)stride, 0);
                ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Table(_context)[47])(_context, buffer, _upload);
                Check(((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Table(_swapChain)[8])(_swapChain, 0, 0));
            }
            finally
            {
                Release(ref buffer);
            }
        }

        private void CreateUpload()
        {
            Release(ref _upload);
            var description = new HdrTextureDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1, Format = 10,
                SampleCount = 1,
            };
            nint texture = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, HdrTextureDescription*, nint, nint*, int>)Table(_device)[5])(
                _device, &description, 0, &texture));
            _upload = texture;
        }

        private static nint* Table(nint instance)
        {
            return *(nint**)instance;
        }

        private static void Check(int result)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        private static nint Query(nint instance, Guid iid)
        {
            nint result = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(instance)[0])(instance, &iid, &result));
            return result;
        }

        private static void Release(ref nint instance)
        {
            if (instance == 0) return;
            ((delegate* unmanaged[Stdcall]<nint, uint>)Table(instance)[2])(instance);
            instance = 0;
        }

        [LibraryImport("d3d11.dll")]
        private static partial int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
            nint featureLevels, uint featureCount, uint sdkVersion, out nint device, out uint featureLevel,
            out nint context);

        [LibraryImport("dxgi.dll")]
        private static partial int CreateDXGIFactory2(uint flags, Guid* iid, nint* factory);
    }
}
