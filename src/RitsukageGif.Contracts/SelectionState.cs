namespace RitsukageGif.Contracts
{
    public sealed class SelectionState
    {
        public PixelRect Selected { get; private set; }
        public PixelPoint Anchor { get; private set; }
        public bool Dragging { get; private set; }

        public void Begin(PixelPoint point)
        {
            Anchor = point;
            Dragging = true;
            Selected = default;
        }

        public void Move(PixelPoint point)
        {
            if (Dragging) Selected = PixelRect.Between(Anchor, point);
        }

        public void End(PixelPoint point, PixelRect hovered)
        {
            Move(point);
            if (point == Anchor) Selected = hovered;
            Dragging = false;
        }

        public void Clear()
        {
            Dragging = false;
            Selected = default;
        }
    }
}
