using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Core
{
    internal sealed class NativeBarcodeOptionsHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal NativeBarcodeOptionsHandle() : base(true)
        {
            SetHandle(NativeBarcodeInterop.Require(NativeBarcodeInterop.CreateOptions()));
        }

        protected override bool ReleaseHandle()
        {
            NativeBarcodeInterop.DeleteOptions(handle);
            return true;
        }
    }
}
