namespace RitsukageGif.Scripting
{
    public sealed record PresetShape(
        string Kind,
        double Radius = 0,
        IReadOnlyList<double>? Points = null,
        string? PathData = null,
        bool EvenOdd = false);
}
