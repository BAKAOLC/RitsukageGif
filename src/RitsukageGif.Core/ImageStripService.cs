namespace RitsukageGif.Core
{
    public static partial class ImageStripService
    {
        public static ImageStripResult Delete(byte[] png, CapturedFrame? hdr, ImageStripAxis axis,
            int start, int count, CancellationToken token = default)
        {
            var raster = PngImageCodec.Decode(png, token);
            if (hdr is not null && (hdr.Width != raster.Width || hdr.Height != raster.Height))
                throw new ArgumentException(UiText.Get("InvalidFrameBuffer"), nameof(hdr));
            var changed = Delete(raster, axis, start, count, token);
            var changedHdr = hdr is null ? null : Delete(hdr, axis, start, count, token);
            return new(PngImageCodec.Encode(changed, token), changedHdr);
        }

        private static void ValidateBand(int width, int height, ImageStripAxis axis, int start, int count)
        {
            var length = axis switch
            {
                ImageStripAxis.Rows => height,
                ImageStripAxis.Columns => width,
                _ => throw new ArgumentOutOfRangeException(nameof(axis)),
            };
            if (start < 0 || start >= length) throw new ArgumentOutOfRangeException(nameof(start));
            if (count <= 0 || count >= length || count > length - start)
                throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
