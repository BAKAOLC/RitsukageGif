namespace RitsukageGif.Desktop
{
    public sealed record PinnedImagePreferences
    {
        public bool Topmost { get; set; } = true;
        public bool ShowWindowShadow { get; set; } = true;
        public bool AutoFit { get; set; } = true;
        public double Opacity { get; set; } = 1;
        public double ZoomStep { get; set; } = 0.1;
        public double FineZoomStep { get; set; } = 0.01;
        public double OpacityStep { get; set; } = 0.05;
        public double MouseThroughOpacity { get; set; } = 0.5;
        public bool SelectableText { get; set; } = true;
        public bool ThumbnailSelectableText { get; set; }
        public bool CopyVisibleRegion { get; set; } = true;
        public bool ResizeFromBorder { get; set; } = true;
        public bool ThumbnailWheelZoom { get; set; } = true;
        public bool ShowMenuShortcuts { get; set; } = true;
        public bool ShowLockIndicator { get; set; } = true;

        public PinnedLockedOperations LockedOperations { get; set; } = PinnedLockedOperations.Move |
                                                                       PinnedLockedOperations.Resize |
                                                                       PinnedLockedOperations.Close |
                                                                       PinnedLockedOperations.Annotation;

        public bool ShowTaskbarWhenNotTopmost { get; set; } = true;
        public int HistoryLimit { get; set; } = 100;
        public PinnedDuplicateBehavior DuplicateBehavior { get; set; } = PinnedDuplicateBehavior.FocusExisting;
        public PinnedRestoreOrder RestoreOrder { get; set; }
        public PinnedZoomAnchor WheelZoomAnchor { get; set; }
        public PinnedZoomAnchor ResetZoomAnchor { get; set; } = PinnedZoomAnchor.Pointer;

        public PinnedImagePreferences Normalize()
        {
            return this with
            {
                Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.15, 1) : 1,
                ZoomStep = double.IsFinite(ZoomStep) ? Math.Clamp(ZoomStep, 0.01, 1) : 0.1,
                FineZoomStep = double.IsFinite(FineZoomStep) ? Math.Clamp(FineZoomStep, 0.001, 0.1) : 0.01,
                OpacityStep = double.IsFinite(OpacityStep) ? Math.Clamp(OpacityStep, 0.01, 0.5) : 0.05,
                MouseThroughOpacity =
                double.IsFinite(MouseThroughOpacity) ? Math.Clamp(MouseThroughOpacity, 0.15, 1) : 0.5,
                HistoryLimit = Math.Max(0, HistoryLimit),
                LockedOperations = LockedOperations & (PinnedLockedOperations.Move | PinnedLockedOperations.Resize |
                                                       PinnedLockedOperations.Close | PinnedLockedOperations.Shortcuts |
                                                       PinnedLockedOperations.Annotation),
                DuplicateBehavior = Enum.IsDefined(DuplicateBehavior)
                    ? DuplicateBehavior
                    : PinnedDuplicateBehavior.FocusExisting,
                RestoreOrder = Enum.IsDefined(RestoreOrder) ? RestoreOrder : PinnedRestoreOrder.ClosedTime,
                WheelZoomAnchor = Enum.IsDefined(WheelZoomAnchor) ? WheelZoomAnchor : PinnedZoomAnchor.TopLeft,
                ResetZoomAnchor = Enum.IsDefined(ResetZoomAnchor) ? ResetZoomAnchor : PinnedZoomAnchor.Pointer,
            };
        }
    }
}
