using System.Numerics;
using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsHdrPresenter
    {
        private nint _compositionDevice, _compositionTarget, _compositionVisual;
        private (int SourceWidth, int SourceHeight, int TargetWidth, int TargetHeight)? _compositionSize;

        private nint CreateCompositionSwapChain(nint factory, nint window, HdrSwapChainDescription* description)
        {
            nint chain = 0;
            try
            {
                Check(((delegate* unmanaged[Stdcall]<nint, nint, HdrSwapChainDescription*, nint, nint*, int>)
                    Table(factory)[24])(factory, _device, description, 0, &chain));
                var dxgiDevice = Query(_device, new("54ec77fa-1377-44e6-8c32-88fd5f44c84c"));
                try
                {
                    var iid = new Guid("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
                    Check(DCompositionCreateDevice(dxgiDevice, &iid, out _compositionDevice));
                }
                finally
                {
                    Release(ref dxgiDevice);
                }

                nint target = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)
                    Table(_compositionDevice)[6])(_compositionDevice, window, 1, &target));
                _compositionTarget = target;
                nint visual = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(_compositionDevice)[7])(
                    _compositionDevice, &visual));
                _compositionVisual = visual;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Table(visual)[15])(visual, chain));
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Table(target)[3])(target, visual));
                Check(((delegate* unmanaged[Stdcall]<nint, int>)Table(_compositionDevice)[3])(_compositionDevice));
                return chain;
            }
            catch
            {
                Release(ref chain);
                throw;
            }
        }

        private void UpdateCompositionTransform(int targetWidth, int targetHeight)
        {
            if (_compositionDevice == 0) return;
            if (targetWidth <= 0) targetWidth = _width;
            if (targetHeight <= 0) targetHeight = _height;
            var size = (_width, _height, targetWidth, targetHeight);
            if (_compositionSize == size) return;
            var transform = Matrix3x2.CreateScale((float)targetWidth / _width, (float)targetHeight / _height);
            Check(((delegate* unmanaged[Stdcall]<nint, Matrix3x2*, int>)Table(_compositionVisual)[8])(
                _compositionVisual, &transform));
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Table(_compositionDevice)[3])(_compositionDevice));
            _compositionSize = size;
        }

        private void DestroyComposition()
        {
            Release(ref _compositionTarget);
            Release(ref _compositionVisual);
            Release(ref _compositionDevice);
            _compositionSize = null;
        }

        [LibraryImport("dcomp.dll")]
        private static partial int DCompositionCreateDevice(nint device, Guid* iid, out nint compositionDevice);
    }
}
