namespace RitsukageGif.Desktop
{
    internal sealed class PinnedImageState : PinnedWindowState
    {
        public string ImageId { get; set; } = "";
        public WindowRoiPinDescriptor? LiveSource { get; set; }
        public bool Thumbnail { get; set; }
        public bool ThumbnailManualRegion { get; set; }
        public double ThumbnailNormalZoom { get; set; }
        public double ThumbnailSavedZoom { get; set; }
        public int ThumbnailStandardWidth { get; set; }
        public int ThumbnailStandardHeight { get; set; }
        public double ThumbnailFullScrollX { get; set; }
        public double ThumbnailFullScrollY { get; set; }
        public int ThumbnailX { get; set; }
        public int ThumbnailY { get; set; }
        public int ThumbnailWidth { get; set; }
        public int ThumbnailHeight { get; set; }
        public bool HasOriginalImage { get; set; }
        public bool HasOriginalSdrImage { get; set; }
        public bool HasOriginalEncoded { get; set; }
        public bool AnimationActive { get; set; }
        public ImageTextMark[] TextMarks { get; set; } = [];
        public ScreenshotAnnotationDocument? Annotations { get; set; }
        public PinnedViewOrientation Orientation { get; set; }
        public PinnedColorMatrixState ColorProcessing { get; set; }
        public bool MarkHidden { get; set; }
        public PinnedImageTranslationState? Translation { get; set; }
        public PinnedImageOcrState? Ocr { get; set; }
        public PinnedTransparentLayerMode? TransparentLayer { get; set; }

        internal PinnedImageState Copy()
        {
            return (PinnedImageState)MemberwiseClone();
        }
    }
}
