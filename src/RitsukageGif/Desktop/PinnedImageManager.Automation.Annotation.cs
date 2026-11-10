namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        internal AnnotationScriptCanvas CreateAutomationCanvas(string id, AnnotationScriptService service)
        {
            return AutomationFind(id) switch
            {
                PinnedImageWindow image => image.CreateAutomationCanvas(service),
                PinnedDocumentWindow document => document.CreateAutomationCanvas(service),
                _ => throw new NotSupportedException(),
            };
        }
    }
}
