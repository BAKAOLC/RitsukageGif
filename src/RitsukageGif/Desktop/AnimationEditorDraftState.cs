namespace RitsukageGif.Desktop
{
    internal sealed class AnimationEditorDraftState
    {
        public required byte[] Pixels { get; init; }
        public required List<AnimationEditorLayer> Layers { get; init; }
        public bool Changed { get; init; }
        public bool RasterChanged { get; init; }
    }
}
