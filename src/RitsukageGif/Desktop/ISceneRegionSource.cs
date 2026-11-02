namespace RitsukageGif.Desktop
{
    internal interface ISceneRegionSource
    {
        bool RegionCaptureSupported => true;

        PixelRect? CoordinateBounds { get; }

        void ConfigureRegion(PixelRect region, int outputWidth, int outputHeight);
    }
}
