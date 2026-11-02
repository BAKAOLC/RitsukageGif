using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed record CapturePreferences
    {
        public bool QuitOnForeignActivation { get; set; }
        public bool CycleRegionHistory { get; set; }
        public bool DisableMouseWheel { get; set; }
        public CaptureBlockedWindowsKeys BlockedWindowsKeys { get; set; }
        public bool PersistHistory { get; set; } = true;
        public CaptureDoubleClickAction DoubleClickAction { get; set; } = CaptureDoubleClickAction.Copy;
        public CaptureDetectionMode DetectionMode { get; set; } = CaptureDetectionMode.Windows;
        public int ElementMargin { get; set; }
        public bool ExpandOnOutsideClick { get; set; } = true;
        public CaptureCursorPolicy CursorPolicy { get; set; }
        public CaptureAuxiliaryPolicy AuxiliaryPolicy { get; set; } = CaptureAuxiliaryPolicy.Alt;
        public bool GuideCursor { get; set; } = true;
        public bool GuideSelectionCenter { get; set; } = true;
        public bool GuideSelectionBorder { get; set; } = true;
        public bool GuideThirds { get; set; }
        public bool GuideMeasurements { get; set; } = true;
        public bool GuideMagnifier { get; set; } = true;
        public bool RelativeCoordinates { get; set; }
        public bool ShowInformation { get; set; } = true;
        public bool ShowMagnifier { get; set; } = true;
        public bool ShowShortcutHints { get; set; } = true;
        public CaptureCodeDetectionMode CodeDetection { get; set; }
        public int RegionHistoryCapacity { get; set; } = 100;
        public int ScreenshotHistoryCapacity { get; set; } = 100;
        public int ScreenshotHistoryMaximumMiB { get; set; } = 128;
        public int Magnification { get; set; } = 11;
        public bool MagnifierGrid { get; set; } = true;
        public bool MagnifierCrosshair { get; set; } = true;
        public bool MagnifierCenterPixel { get; set; } = true;
        public bool MagnifierSelectionBorder { get; set; } = true;
        public bool MagnifierSelectionHandles { get; set; }
        public bool MagnifierCoordinates { get; set; } = true;
        public bool MagnifierShortcuts { get; set; } = true;
        public string MagnifierGridColor { get; set; } = "#46FFFFFF";

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<ColorFormatDefinition> ColorFormats { get; set; } = SampleColorFormats.CreateDefaults();
        public string MagnifierColorFormatId { get; set; } = "rgb";
        public bool SmoothMagnifier { get; set; }
        public bool KeepDecoration { get; set; }
        public CaptureDecoration RememberedDecoration { get; set; } = CaptureDecoration.None;
        public bool FrameRoundEnabled { get; set; }
        public double FrameRoundRadius { get; set; } = 12;

        public CapturePreferences Normalize()
        {
            var formats = SampleColorFormats.Normalize(ColorFormats);
            return this with
            {
                DoubleClickAction =
                Enum.IsDefined(DoubleClickAction) ? DoubleClickAction : CaptureDoubleClickAction.Copy,
                DetectionMode = Enum.IsDefined(DetectionMode) ? DetectionMode : CaptureDetectionMode.Windows,
                CodeDetection = Enum.IsDefined(CodeDetection) ? CodeDetection : CaptureCodeDetectionMode.Disabled,
                CursorPolicy = Enum.IsDefined(CursorPolicy) ? CursorPolicy : CaptureCursorPolicy.Hidden,
                AuxiliaryPolicy = Enum.IsDefined(AuxiliaryPolicy) ? AuxiliaryPolicy : CaptureAuxiliaryPolicy.Alt,
                RegionHistoryCapacity = Math.Clamp(RegionHistoryCapacity, 0, 1000),
                ScreenshotHistoryCapacity = Math.Clamp(ScreenshotHistoryCapacity, 0, 1000),
                ScreenshotHistoryMaximumMiB = Math.Clamp(ScreenshotHistoryMaximumMiB, 0, 512),
                ElementMargin = Math.Clamp(ElementMargin, -128, 128),
                Magnification = Math.Clamp(Magnification, 2, 32),
                MagnifierGridColor = Color.TryParse(MagnifierGridColor, out var color) ? color.ToString() : "#46FFFFFF",
                BlockedWindowsKeys = BlockedWindowsKeys & CaptureBlockedWindowsKeys.All,
                ColorFormats = formats,
                MagnifierColorFormatId = SampleColorFormats.First(formats, MagnifierColorFormatId).Id,
                RememberedDecoration = NormalizeDecoration(RememberedDecoration),
                FrameRoundRadius = double.IsFinite(FrameRoundRadius) ? Math.Clamp(FrameRoundRadius, 0, 60) : 12,
            };
        }

        private static CaptureDecoration NormalizeDecoration(CaptureDecoration? decoration)
        {
            return decoration is not null && Enum.IsDefined(decoration.Kind) && decoration.Strength is >= 0 and <= 50 &&
                   (decoration.Kind == CaptureDecorationKind.None || decoration.Strength > 0)
                ? decoration with { CornerRadius = 0 }
                : CaptureDecoration.None;
        }
    }
}
