namespace RitsukageGif.Desktop
{
    public sealed record GlobalMouseBinding
    {
        public string ActionId { get; set; } = "";
        public string Gesture { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }
}
