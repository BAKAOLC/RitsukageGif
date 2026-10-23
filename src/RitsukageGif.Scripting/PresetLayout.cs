namespace RitsukageGif.Scripting
{
    public sealed record PresetLayout(int Width, int Height, string Background, IReadOnlyList<PresetLayer> Layers);
}
