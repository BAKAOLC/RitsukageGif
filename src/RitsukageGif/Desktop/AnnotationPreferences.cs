using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed record AnnotationPreferences
    {
        public string Color { get; set; } = "#ffff0000";
        public double Thickness { get; set; } = 4;
        public double FontSize { get; set; } = 24;
        public int MosaicSize { get; set; } = 12;
        public bool RememberLastTool { get; set; }
        public bool LimitToSelection { get; set; } = true;
        public string LastTool { get; set; } = "Select";
        public bool SharedColor { get; set; } = true;
        public ScreenshotAnnotationProperties Properties { get; set; } = new();
        public Dictionary<string, AnnotationToolStyle> ToolStyles { get; set; } = [];

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<string> ColorPalette { get; set; } =
            ["#ffff0000", "#ffffa500", "#ffffff00", "#ff008000", "#ff00bfff", "#ff800080", "#ffffffff", "#ff000000"];

        public bool SecondaryEditingEnabled { get; set; } = true;
        public bool QuickSettingsEnabled { get; set; } = true;
        public AnnotationPencilCursor PencilCursor { get; set; }
        public double MaximumBrushThickness { get; set; } = 100;
        public bool RememberPinnedTool { get; set; }
        public string PinnedLastTool { get; set; } = "Select";
        public bool DisableWheelToolAdjustment { get; set; }
        public double QuickSettingsPadding { get; set; }
        public AnnotationControlPointVisibility ControlPointVisibility { get; set; }
        public bool RenumberOnDelete { get; set; } = true;
        public bool NumberStartsAtZero { get; set; }
        public bool GlobalNumberCounter { get; set; }
        public bool SmoothBrush { get; set; } = true;
        public AnnotationPencilShiftMode PencilShiftMode { get; set; }
        public PenLineJoin PolylineJoin { get; set; } = PenLineJoin.Round;
        public PenLineCap PolylineCap { get; set; } = PenLineCap.Round;
        public bool ArrowDoubleClickCommentEnabled { get; set; } = true;
        public bool ArrowCurveControlPoints { get; set; } = true;
        public AnnotationTextWrapMode TextWrapMode { get; set; }
        public bool EraserSecondaryEditingEnabled { get; set; } = true;
        public bool AutoSelectPrivacyMosaic { get; set; } = true;
        public ToolbarPreferences Toolbar { get; set; } = new();

        public AnnotationPreferences Normalize()
        {
            return this with
            {
                Color = Avalonia.Media.Color.TryParse(Color, out var color) ? color.ToString() : Colors.Red.ToString(),
                Thickness = double.IsFinite(Thickness) ? Math.Clamp(Thickness, 1, 1024) : 4,
                FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 1, 1024) : 24,
                MosaicSize = Math.Clamp(MosaicSize, 2, 256),
                MaximumBrushThickness = double.IsFinite(MaximumBrushThickness)
                    ? Math.Clamp(MaximumBrushThickness, 1, 1024)
                    : 100,
                QuickSettingsPadding =
                double.IsFinite(QuickSettingsPadding) ? Math.Clamp(QuickSettingsPadding, 0, 64) : 0,
                ColorPalette = NormalizePalette(ColorPalette),
                PinnedLastTool = Enum.TryParse<ScreenshotTool>(PinnedLastTool, out var pinnedTool) &&
                                 Enum.IsDefined(pinnedTool)
                    ? pinnedTool.ToString()
                    : "Select",
                PencilCursor = Enum.IsDefined(PencilCursor) ? PencilCursor : AnnotationPencilCursor.CrosshairRing,
                ControlPointVisibility = Enum.IsDefined(ControlPointVisibility)
                    ? ControlPointVisibility
                    : AnnotationControlPointVisibility.Hover,
                PencilShiftMode = Enum.IsDefined(PencilShiftMode)
                    ? PencilShiftMode
                    : AnnotationPencilShiftMode.FreeAngleLine,
                PolylineJoin = Enum.IsDefined(PolylineJoin) ? PolylineJoin : PenLineJoin.Round,
                PolylineCap = Enum.IsDefined(PolylineCap) ? PolylineCap : PenLineCap.Round,
                TextWrapMode = Enum.IsDefined(TextWrapMode) ? TextWrapMode : AnnotationTextWrapMode.WordOrCharacter,
                Properties = Properties.Normalize(),
                ToolStyles = ToolStyles.Where(pair => Enum.TryParse<ScreenshotTool>(pair.Key, out var key) &&
                                                      Enum.IsDefined(key) && pair.Key == key.ToString() &&
                                                      pair.Value is not null)
                    .ToDictionary(pair => Enum.Parse<ScreenshotTool>(pair.Key).ToString(),
                        pair => pair.Value.Normalize()),
                LastTool = Enum.TryParse<ScreenshotTool>(LastTool, out var tool) && Enum.IsDefined(tool)
                    ? tool.ToString()
                    : "Select",
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                Toolbar = (Toolbar ?? new ToolbarPreferences()).Normalize(),
            };
        }

        private static List<string> NormalizePalette(IEnumerable<string>? palette)
        {
            var colors = (palette ?? []).Where(value => Avalonia.Media.Color.TryParse(value, out _))
                .Select(value => Avalonia.Media.Color.Parse(value).ToString()).Distinct().Take(32).ToList();
            return colors.Count == 0
                ?
                [
                    "#ffff0000", "#ffffa500", "#ffffff00", "#ff008000", "#ff00bfff", "#ff800080", "#ffffffff",
                    "#ff000000",
                ]
                : colors;
        }
    }
}
