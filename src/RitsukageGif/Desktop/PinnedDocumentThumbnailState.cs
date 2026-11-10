namespace RitsukageGif.Desktop
{
    internal sealed record PinnedDocumentThumbnailState(
        bool Enabled,
        double NormalWidth,
        double NormalHeight,
        double SurfaceWidth,
        double SurfaceHeight,
        double ScrollX,
        double ScrollY,
        double NormalZoom,
        double ThumbnailZoom,
        double RegionX,
        double RegionY,
        double RegionWidth,
        double RegionHeight,
        bool ManualRegion)
    {
        internal bool IsValid => double.IsFinite(NormalWidth) && NormalWidth > 0 &&
                                 double.IsFinite(NormalHeight) && NormalHeight > 0 &&
                                 double.IsFinite(SurfaceWidth) && SurfaceWidth > 0 &&
                                 double.IsFinite(SurfaceHeight) && SurfaceHeight > 0 &&
                                 double.IsFinite(ScrollX) && ScrollX >= 0 &&
                                 double.IsFinite(ScrollY) && ScrollY >= 0 &&
                                 double.IsFinite(NormalZoom) && NormalZoom is >= .05 and <= 8 &&
                                 double.IsFinite(ThumbnailZoom) && ThumbnailZoom >= .05 &&
                                 double.IsFinite(RegionX) && RegionX >= 0 &&
                                 double.IsFinite(RegionY) && RegionY >= 0 &&
                                 double.IsFinite(RegionWidth) && RegionWidth > 0 &&
                                 double.IsFinite(RegionHeight) && RegionHeight > 0;
    }
}
