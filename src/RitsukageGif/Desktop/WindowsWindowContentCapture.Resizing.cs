namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsWindowContentCapture
    {
        private void RecreatePool(Size size)
        {
            Logger.ZLogDebug(
                $"Window capture frame pool resized: previousWidth={_size.Width}, previousHeight={_size.Height}, width={size.Width}, height={size.Height}");
            _pixels = null;
            _colorFrame = null;
            _size = size;
            Release(ref _staging);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Size, int>)Vtable(_pool)[6])(_pool,
                _winrtDevice, _format, 2, size));
        }
    }
}
