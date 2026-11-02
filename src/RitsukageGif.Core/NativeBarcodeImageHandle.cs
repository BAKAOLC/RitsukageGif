using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Core
{
    internal sealed class NativeBarcodeImageHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal NativeBarcodeImageHandle(nint value) : base(true)
        {
            SetHandle(NativeBarcodeInterop.Require(value));
        }

        protected override bool ReleaseHandle()
        {
            NativeBarcodeInterop.DeleteImage(handle);
            return true;
        }
    }
}
