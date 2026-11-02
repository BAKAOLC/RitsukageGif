namespace RitsukageGif.Desktop
{
    internal sealed partial class MacAutomaticScrollInput(WindowTarget target, int process) : IAutomaticScrollInput
    {
        public string? UnavailableReason { get; private set; }
        private static bool HasPermission => Native.CgPreflightPostEventAccess();

        public AutomaticScrollInputResult Scroll(ScrollCaptureAxis axis, PixelRect region, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            UnavailableReason = null;
            if (!HasPermission) return Unavailable("Permission to post pointer events was denied or revoked.");
            var current = WindowLocator.Refresh(target);
            if (current is null || current.Client != target.Client || region.IsEmpty ||
                region.Intersect(current.Client) != region)
                return Unavailable("The selected window or capture region changed.");
            if (!Native.TryGetPointer(out var pointer) || pointer.X < region.X || pointer.Y < region.Y ||
                pointer.X >= region.Right || pointer.Y >= region.Bottom)
                return Unavailable("Move the pointer inside the capture region to use automatic scrolling.");
            if (!Native.ReadWindows(target.Handle, pointer, out var owner, out var front) || owner != process ||
                front != target.Handle)
                return Unavailable("The pointer is no longer over the selected window.");
            token.ThrowIfCancellationRequested();
            if (Native.PostScroll(process, axis, pointer)) return AutomaticScrollInputResult.Sent;
            UnavailableReason = "The pointer scroll event could not be created.";
            return AutomaticScrollInputResult.Failed;
        }

        internal static bool RequestPermission()
        {
            return Native.CgRequestPostEventAccess();
        }

        internal static AutomaticScrollConnection Create(PixelRect region, WindowTarget? source)
        {
            if (!HasPermission)
                return new(null, "Permission to post pointer events is required.", true);
            if (region.IsEmpty) return new(null, "The capture region is empty.");
            var center = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
            var target = source ?? WindowLocator.List().FirstOrDefault(window => window.Client.Contains(center));
            if (target is null || region.Intersect(target.Client) != region)
                return new(null, "The capture region must remain inside the selected window.");
            if (!Native.TryGetPointer(out var pointer) ||
                !Native.ReadWindows(target.Handle, pointer, out var process, out _))
                return new(null, "The selected window is no longer available.");
            return new(new MacAutomaticScrollInput(target, process));
        }

        private AutomaticScrollInputResult Unavailable(string reason)
        {
            UnavailableReason = reason;
            return AutomaticScrollInputResult.Unavailable;
        }
    }
}
