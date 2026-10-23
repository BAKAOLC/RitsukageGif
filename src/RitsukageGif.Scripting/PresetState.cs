namespace RitsukageGif.Scripting
{
    public sealed record PresetState(
        string Name,
        IReadOnlyList<PresetAction> Enter,
        IReadOnlyList<PresetAction> Exit,
        IReadOnlyList<PresetRule> Rules);
}
