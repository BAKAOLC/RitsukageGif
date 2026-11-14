using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed record PinnedImagePreferences
    {
        public bool Topmost { get; set; } = true;
        public bool ShowWindowShadow { get; set; } = true;
        public PinnedTitlePosition TitlePosition { get; set; }
        public PinnedDisplayPosition DisplayPosition { get; set; }
        public bool HideOthersOnShake { get; set; }
        public bool KeepOthersHiddenOnNewPin { get; set; }
        public bool SkipNegativeSpacingConfirmation { get; set; }
        public bool ActivateOnNewPin { get; set; } = true;
        public bool FlashOnNewPin { get; set; }
        public bool FlashAfterGroupSwitch { get; set; }
        public bool TranslucentOnAttention { get; set; } = true;
        public string ActiveGroup { get; set; } = "";

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<PinnedGroupDefinition> Groups { get; set; } = [];
        public bool AutoFit { get; set; } = true;
        public bool SmoothScaling { get; set; }
        public PinnedTransparentLayerMode TransparentLayer { get; set; } = PinnedTransparentLayerMode.Checkerboard;
        public int MaximumWindowSize { get; set; } = PinnedViewportGeometry.DefaultMaximumWindowSize;
        public int? QuickThumbnailWidth { get; set; }
        public int? QuickThumbnailHeight { get; set; }
        public double Opacity { get; set; } = 1;
        public double ZoomStep { get; set; } = 0.1;
        public double FineZoomStep { get; set; } = 0.01;
        public double OpacityStep { get; set; } = 0.05;
        public double MouseThroughOpacity { get; set; } = 0.5;
        public bool ShowMouseThroughToolbar { get; set; }
        public bool SelectableText { get; set; } = true;
        public bool ThumbnailSelectableText { get; set; }
        public bool ShowTextPopupMenu { get; set; } = true;
        public bool ConfirmLongTextPaste { get; set; } = true;

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<string> TextHighlightColors { get; set; } =
            ["#FF909C", "#B89FFF", "#74B4FF", "#5AD8A6", "#F6BD16"];

        public PinnedImageOutputContent OutputContent { get; set; }
        public PinnedReplacementAction ReplacementAction { get; set; }
        public bool ResizeFromBorder { get; set; } = true;
        public bool ThumbnailWheelZoom { get; set; } = true;
        public bool ShowMenuShortcuts { get; set; } = true;
        public bool ShowLockIndicator { get; set; } = true;

        public PinnedLockRestrictions LockRestrictions { get; set; }

        public bool ShowTaskbarWhenNotTopmost { get; set; } = true;
        public int HistoryLimit { get; set; } = 100;
        public PinnedDuplicateBehavior DuplicateBehavior { get; set; } = PinnedDuplicateBehavior.FocusExisting;
        public PinnedRestoreOrder RestoreOrder { get; set; }
        public PinnedZoomAnchor WheelZoomAnchor { get; set; }
        public PinnedZoomAnchor ResetZoomAnchor { get; set; } = PinnedZoomAnchor.Pointer;
        public PinnedContentKinds SupportedContentKinds { get; set; } = PinnedContentKinds.All;

        public PinnedImagePreferences Normalize()
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            var groups = (Groups ?? []).Where(item => item is { IsValid: true }).Select(item => item.Normalize())
                .DistinctBy(item => item.Id, StringComparer.Ordinal)
                .DistinctBy(item => item.Name, StringComparer.Ordinal).ToList();
            return this with
            {
                TransparentLayer = Enum.IsDefined(TransparentLayer)
                    ? TransparentLayer
                    : PinnedTransparentLayerMode.Checkerboard,
                Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.1, 1) : 1,
                ZoomStep = double.IsFinite(ZoomStep) ? Math.Clamp(ZoomStep, 0.01, 1) : 0.1,
                FineZoomStep = double.IsFinite(FineZoomStep) ? Math.Clamp(FineZoomStep, 0.001, 0.1) : 0.01,
                OpacityStep = double.IsFinite(OpacityStep) ? Math.Clamp(OpacityStep, 0.01, 0.5) : 0.05,
                MouseThroughOpacity =
                double.IsFinite(MouseThroughOpacity) ? Math.Clamp(MouseThroughOpacity, 0.15, 1) : 0.5,
                HistoryLimit = Math.Max(0, HistoryLimit),
                MaximumWindowSize = Math.Max(0, MaximumWindowSize),
                QuickThumbnailWidth = QuickThumbnailWidth is { } width ? Math.Max(1, width) : null,
                QuickThumbnailHeight = QuickThumbnailHeight is { } height ? Math.Max(1, height) : null,
                TitlePosition = Enum.IsDefined(TitlePosition) ? TitlePosition : PinnedTitlePosition.BottomCenter,
                DisplayPosition = Enum.IsDefined(DisplayPosition)
                    ? DisplayPosition
                    : PinnedDisplayPosition.ScreenshotLocation,
                ActiveGroup = ActiveGroup is { Length: 0 } || groups.Any(group => group.Id == ActiveGroup)
                    ? ActiveGroup
                    : "",
                Groups = groups,
                TextHighlightColors = NormalizeHighlightColors(TextHighlightColors),
                SupportedContentKinds = SupportedContentKinds & PinnedContentKinds.All,
                OutputContent = Enum.IsDefined(OutputContent) ? OutputContent : PinnedImageOutputContent.Current,
                ReplacementAction = Enum.IsDefined(ReplacementAction)
                    ? ReplacementAction
                    : PinnedReplacementAction.KeepSource,
                LockRestrictions = (LockRestrictions &
                                    ~(PinnedLockRestrictions.Move | PinnedLockRestrictions.PointerClose |
                                      PinnedLockRestrictions.KeyboardClose | PinnedLockRestrictions.IncomingDrop)) == 0
                    ? LockRestrictions
                    : PinnedLockRestrictions.None,
                DuplicateBehavior = Enum.IsDefined(DuplicateBehavior)
                    ? DuplicateBehavior
                    : PinnedDuplicateBehavior.FocusExisting,
                RestoreOrder = Enum.IsDefined(RestoreOrder) ? RestoreOrder : PinnedRestoreOrder.ClosedTime,
                WheelZoomAnchor = Enum.IsDefined(WheelZoomAnchor) ? WheelZoomAnchor : PinnedZoomAnchor.TopLeft,
                ResetZoomAnchor = Enum.IsDefined(ResetZoomAnchor) ? ResetZoomAnchor : PinnedZoomAnchor.Pointer,
            };
        }

        private static List<string> NormalizeHighlightColors(IEnumerable<string>? colors)
        {
            var palette = (colors ?? []).Where(value => Color.TryParse(value, out _))
                .Select(value => Color.Parse(value).ToString()).ToList();
            return palette.Count > 0 ? palette : ["#ffffff00", "#ff00ff00", "#ff00ffff", "#ffff00ff", "#ffffa500"];
        }
    }
}
