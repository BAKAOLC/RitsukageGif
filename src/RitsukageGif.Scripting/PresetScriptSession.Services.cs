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
        internal IDocumentOutputService? DocumentService => documentService;
        internal ITableRecognitionService? TableService => tableService;
        internal IFormulaRecognitionService? FormulaService => formulaService;
        internal IFormulaOutputService? FormulaOutputService => formulaOutputService;
        internal ITableOutputService? TableOutputService => tableOutputService;
        internal IFormulaDocumentService? FormulaDocumentService => formulaDocumentService;
        internal ITableDataService? TableDataService => tableDataService;
        internal ITableEditService? TableEditService => tableEditService;
    }
}
