namespace RitsukageGif.Contracts
{
    public sealed record DocumentMargins
    {
        public double Left { get; init; }
        public double Top { get; init; }
        public double Right { get; init; }
        public double Bottom { get; init; }
    }
}
