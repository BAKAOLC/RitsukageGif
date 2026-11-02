namespace RitsukageGif.Desktop
{
    internal readonly record struct PinnedRichLength(double Value, bool Percent = false)
    {
        internal static PinnedRichLength Auto => new(double.NaN);

        internal double Resolve(double available, double automatic)
        {
            return double.IsNaN(Value) ? automatic : Percent ? available * Value / 100 : Value;
        }
    }
}
