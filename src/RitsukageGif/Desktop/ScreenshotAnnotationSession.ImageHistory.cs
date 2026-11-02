namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotAnnotationSession
    {
        internal const long ImageHistoryAssetBudget = AnnotationImageBudget;
        internal Func<bool>? DocumentCanUndo { get; set; }
        internal Func<bool>? DocumentCanRedo { get; set; }
        internal Action? DocumentUndo { get; set; }
        internal Action? DocumentRedo { get; set; }
        internal Action? DocumentBranchChanged { get; set; }

        internal IEnumerable<ScreenshotAnnotationImage> ImageHistoryAssets =>
            _retained.Select(annotation => annotation.WatermarkImage).Append(_watermarkSource)
                .OfType<ScreenshotAnnotationImage>();

        internal void SuspendImageSourceForHistory()
        {
            CancelWork();
            _pixelPreviewWork?.Cancel();
            _frames.Clear();
            _overlayFrame = null;
            _pixelPreviewFrames = [];
            _pixelPreviewState = [];
            ClearImagePreviewCache();
            foreach (var annotation in _retained)
            {
                foreach (var tile in annotation.ImageTiles) tile.Image.Dispose();
                annotation.ImageTiles = [];
                annotation.MagnifierPreview?.Dispose();
                annotation.MagnifierPreview = null;
                annotation.MagnifierSourceState = null;
            }
        }

        internal ScreenshotAnnotationSession CreateImageDocumentSession()
        {
            var session = new ScreenshotAnnotationSession { CanScroll = false };
            session.RestoreImageDocumentControls(this);
            return session;
        }

        internal void RestoreImageDocumentControls(ScreenshotAnnotationSession source)
        {
            ApplyPreferences(source.CapturePreferences(source._behavior));
            _watermarkSource?.Dispose();
            _watermarkSource = source._watermarkSource?.Retain();
            _secondaryEditingOverride = source._secondaryEditingOverride;
            CanOutputHdr = source.CanOutputHdr;
            OutputColorMode = source.OutputColorMode;
            Tool = ScreenshotTool.Select;
        }
    }
}
