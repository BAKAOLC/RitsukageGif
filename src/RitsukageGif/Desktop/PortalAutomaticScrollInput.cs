namespace RitsukageGif.Desktop
{
    internal sealed class PortalAutomaticScrollInput(PortalAutomaticScrollBinding binding) : IAutomaticScrollInput
    {
        public string? UnavailableReason { get; private set; }

        public bool Prepare(PixelRect region, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            UnavailableReason = null;
            if (!binding.IsCurrent() || region.IsEmpty || region.Intersect(binding.CoordinateBounds) != region)
            {
                UnavailableReason = binding.FaultReason() ?? "The authorized capture source changed.";
                return false;
            }

            if (binding.PreparePointer(region, token) == AutomaticScrollInputResult.Sent && binding.IsCurrent())
                return true;
            UnavailableReason = binding.FaultReason() ?? "The portal rejected pointer preparation.";
            return false;
        }

        public AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            UnavailableReason = null;
            if (!binding.IsCurrent() || region.IsEmpty || region.Intersect(binding.CoordinateBounds) != region)
                return Unavailable(binding.FaultReason() ?? "The authorized capture source changed.");
            var pointer = binding.ReadPointer();
            if (pointer is not { Valid: true } || pointer.Generation != binding.Generation ||
                pointer.StreamId != binding.StreamId || !region.Contains(pointer.Position))
                return Unavailable(
                    "The authorized source has no current pointer observation inside the capture region.");
            var result = binding.SendScroll(axis, region, token);
            if (result is AutomaticScrollInputResult.Unavailable or AutomaticScrollInputResult.Failed)
                UnavailableReason = binding.FaultReason() ?? "The portal rejected the pointer scroll event.";
            return result;
        }

        private AutomaticScrollInputResult Unavailable(string reason)
        {
            UnavailableReason = reason;
            return AutomaticScrollInputResult.Unavailable;
        }
    }
}
