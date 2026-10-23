namespace RitsukageGif.Scripting
{
    public sealed record PresetRule(
        string Event,
        double Delay,
        PresetExpression? Condition,
        IReadOnlyList<PresetAction> Actions);
}
