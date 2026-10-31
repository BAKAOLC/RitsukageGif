namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class WindowsWindowContentCapture
    {
        private void RecreatePool(Size size, bool retainFrame = false)
        {
            Logger.ZLogDebug(
                $"Window capture frame pool resized: previousWidth={_size.Width}, previousHeight={_size.Height}, width={size.Width}, height={size.Height}");
            if (!retainFrame)
            {
                _pixels = null;
                _linearPixels = null;
                _colorFrame = null;
            }

            _size = size;
            Release(ref _staging);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Size, int>)Vtable(_pool)[6])(_pool,
                _winrtDevice, _format, 2, size));
        }

        private byte[] LinearPixels(Size size)
        {
            var length = checked(size.Width * size.Height * 8);
            if (_retainFrames) return new byte[length];
            if (_linearPixels?.Length != length) _linearPixels = new byte[length];
            return _linearPixels;
        }

        private static void CopyMappedRows(MappedResource source, byte[] destination, int width, int height,
            int destinationWidth, int pixelBytes)
        {
            var sourceRowBytes = checked(width * pixelBytes);
            var destinationRowBytes = checked(destinationWidth * pixelBytes);
            for (var y = 0; y < height; y++)
                new ReadOnlySpan<byte>((byte*)source.Data + y * source.RowPitch, sourceRowBytes)
                    .CopyTo(destination.AsSpan(y * destinationRowBytes, sourceRowBytes));
        }
    }
}
