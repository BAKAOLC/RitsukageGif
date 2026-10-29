namespace RitsukageGif.Core
{
    public static partial class PngImageCodec
    {
        private static int ValidateRasterMetadata(PngRaster raster)
        {
            var paletteCount = 0;
            var transparencyCount = 0;
            var paletteEntries = 0;
            var transparencyLength = -1;
            foreach (var (type, data) in raster.Metadata)
                switch (type)
                {
                    case "PLTE":
                        paletteCount++;
                        if (data.Length is 0 or > 768 || data.Length % 3 != 0) throw new InvalidDataException();
                        paletteEntries = data.Length / 3;
                        break;
                    case "tRNS":
                        transparencyCount++;
                        transparencyLength = data.Length;
                        break;
                }

            if (paletteCount > 1 || transparencyCount > 1 ||
                (raster.ColorType == 3 && (paletteEntries == 0 || paletteEntries > 1 << raster.Depth)) ||
                (raster.ColorType is 0 or 4 && paletteCount != 0) ||
                (transparencyCount != 0 && raster.ColorType switch
                {
                    0 => transparencyLength != 2,
                    2 => transparencyLength != 6,
                    3 => transparencyLength <= 0 || transparencyLength > paletteEntries,
                    _ => true,
                }))
                throw new InvalidDataException();
            return paletteEntries;
        }
    }
}
