namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        internal IMediaProcessingService? MediaService => mediaService;
        internal IImageCompositionService? ImageService => imageService;
        internal IStaticCaptureService? StaticCaptureService => staticCaptureService;
        internal IVideoRecordingService? VideoService => videoService;
        internal ITextRecognitionService? TextRecognitionService => textRecognitionService;
        internal IBarcodeRecognitionService? BarcodeRecognitionService => barcodeRecognitionService;
        internal IPrivacyDetectionService? PrivacyService => privacyService;
    }
}
