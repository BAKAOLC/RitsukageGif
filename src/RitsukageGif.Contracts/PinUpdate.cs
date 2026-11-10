namespace RitsukageGif.Contracts
{
    public sealed record PinUpdate
    {
        public double? ScrollX { get; init; }
        public double? ScrollY { get; init; }
        public PinTransparentLayer? TransparentLayer { get; init; }
        public int? X { get; init; }
        public int? Y { get; init; }
        public double? Zoom { get; init; }
        public double? Opacity { get; init; }
        public bool? Visible { get; init; }
        public bool? Topmost { get; init; }
        public bool? Locked { get; init; }
        public bool? MouseThrough { get; init; }
        public string? Group { get; init; }
        public string? Title { get; init; }
        public bool? Thumbnail { get; init; }
        public bool? Shadow { get; init; }
        public bool? AnnotationsVisible { get; init; }
        public bool? Selectable { get; init; }
        public bool? IgnoreFormat { get; init; }
    }
}
