namespace RitsukageGif.Scripting
{
    public sealed record PresetFunction(
        string Name,
        IReadOnlyList<string> Parameters,
        IReadOnlyList<PresetAction> Body);
}
