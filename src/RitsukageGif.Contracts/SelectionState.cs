namespace RitsukageGif.Contracts
{
    public sealed class SelectionState
    {
        private double _dragThreshold;
        private PixelPoint _pointerAnchor;
        public PixelRect Selected { get; private set; }
        private PixelPoint Anchor { get; set; }
        public bool Dragging { get; private set; }
        public bool DragThresholdReached { get; private set; }

        public void Begin(PixelPoint point, PixelPoint pointer, double dragThreshold)
        {
            Anchor = point;
            _pointerAnchor = pointer;
            _dragThreshold = Math.Max(0, dragThreshold);
            DragThresholdReached = false;
            Dragging = true;
            Selected = default;
        }

        public void Move(PixelPoint point, PixelPoint pointer)
        {
            if (!Dragging) return;
            var dx = (double)pointer.X - _pointerAnchor.X;
            var dy = (double)pointer.Y - _pointerAnchor.Y;
            DragThresholdReached |= dx * dx + dy * dy > _dragThreshold * _dragThreshold;
            if (DragThresholdReached) Selected = PixelRect.Between(Anchor, point);
        }

        public void End(PixelPoint point, PixelPoint pointer, PixelRect hovered)
        {
            Move(point, pointer);
            if (!DragThresholdReached) Selected = hovered;
            Dragging = false;
            DragThresholdReached = false;
        }

        public void UpdateDraggedRegion(PixelRect region)
        {
            if (Dragging && DragThresholdReached) Selected = region;
        }

        public void Clear()
        {
            Dragging = false;
            DragThresholdReached = false;
            Selected = default;
        }

        public void Set(PixelRect region)
        {
            Dragging = false;
            DragThresholdReached = false;
            Selected = region;
        }
    }
}
