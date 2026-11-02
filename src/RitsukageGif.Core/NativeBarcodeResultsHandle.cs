using Microsoft.Win32.SafeHandles;

namespace RitsukageGif.Core
{
    internal sealed class NativeBarcodeResultsHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal NativeBarcodeResultsHandle(nint value) : base(true)
        {
            SetHandle(NativeBarcodeInterop.Require(value));
        }

        protected override bool ReleaseHandle()
        {
            NativeBarcodeInterop.DeleteResults(handle);
            return true;
        }
    }
}
