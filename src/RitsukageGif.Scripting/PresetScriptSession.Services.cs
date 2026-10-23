namespace RitsukageGif.Scripting
{
    public sealed partial class PresetScriptSession
    {
        internal IMediaProcessingService? MediaService => mediaService;
        internal IImageCompositionService? ImageService => imageService;
    }
}
