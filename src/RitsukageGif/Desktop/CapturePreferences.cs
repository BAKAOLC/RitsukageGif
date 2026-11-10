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
        public CaptureClickAction DoubleClickAction { get; set; } = CaptureClickAction.Copy;
        public CaptureClickAction MiddleClickAction { get; set; } = CaptureClickAction.Pin;
        public CaptureWheelFunctions WheelFunctions { get; set; } = CaptureWheelFunctions.All;
        public bool PinAndCopy { get; set; }
        public bool ShowSecondaryButtons { get; set; } = true;
        public int PolygonCornerRadius { get; set; } = 5;
        public ScreenshotSoundTiming SoundTiming { get; set; }
        public CaptureDetectionMode DetectionMode { get; set; } = CaptureDetectionMode.Controls;
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
        public SelectionSizeUnit InformationUnit { get; set; }
        public SelectionInfoItems InformationItems { get; set; } = SelectionInfoItems.All;
        public bool ShowMagnifier { get; set; } = true;
        public bool ShowShortcutHints { get; set; } = true;
        public CaptureCodeDetectionMode CodeDetection { get; set; } = CaptureCodeDetectionMode.Automatic;
        public int RegionHistoryCapacity { get; set; } = 100;
        public int ScreenshotHistoryCapacity { get; set; } = 100;
        public int ScreenshotHistoryMaximumMiB { get; set; } = 128;
        public int Magnification { get; set; } = 11;
        public bool MagnifierGrid { get; set; } = true;
        public bool MagnifierCrosshair { get; set; } = true;
        public bool MagnifierCenterPixel { get; set; } = true;
        public bool MagnifierSelectionBorder { get; set; } = true;
        public bool MagnifierSelectionHandles { get; set; }
        public bool MagnifierAnnotations { get; set; } = true;
        public bool MagnifierAnnotationControls { get; set; } = true;
        public bool MagnifierCoordinates { get; set; } = true;
        public bool MagnifierShortcuts { get; set; } = true;
        public string MagnifierGridColor { get; set; } = "#80808080";

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public List<ColorFormatDefinition> ColorFormats { get; set; } = SampleColorFormats.CreateDefaults();
        public string MagnifierColorFormatId { get; set; } = "rgb";
        public bool SmoothMagnifier { get; set; }
        public bool KeepDecoration { get; set; }
        public CaptureFrameDecorationPreferences FrameDecoration { get; set; } = new();
        public bool FrameRoundEnabled { get; set; }
        public double FrameRoundRadius { get; set; }

        public CapturePreferences Normalize()
        {
            var formats = SampleColorFormats.Normalize(ColorFormats);
            return this with
            {
                DoubleClickAction =
                Enum.IsDefined(DoubleClickAction) ? DoubleClickAction : CaptureClickAction.Copy,
                MiddleClickAction = Enum.IsDefined(MiddleClickAction) ? MiddleClickAction : CaptureClickAction.Pin,
                WheelFunctions = WheelFunctions & CaptureWheelFunctions.All,
                SoundTiming = SoundTiming & ScreenshotSoundTiming.All,
                DetectionMode = Enum.IsDefined(DetectionMode) ? DetectionMode : CaptureDetectionMode.Controls,
                CodeDetection = Enum.IsDefined(CodeDetection) ? CodeDetection : CaptureCodeDetectionMode.Automatic,
                CursorPolicy = Enum.IsDefined(CursorPolicy) ? CursorPolicy : CaptureCursorPolicy.Hidden,
                AuxiliaryPolicy = Enum.IsDefined(AuxiliaryPolicy) ? AuxiliaryPolicy : CaptureAuxiliaryPolicy.Alt,
                InformationUnit = Enum.IsDefined(InformationUnit) ? InformationUnit : SelectionSizeUnit.Pixels,
                InformationItems = InformationItems & SelectionInfoItems.All,
                RegionHistoryCapacity = Math.Clamp(RegionHistoryCapacity, 0, 99999),
                ScreenshotHistoryCapacity = Math.Clamp(ScreenshotHistoryCapacity, 0, 99999),
                ScreenshotHistoryMaximumMiB = Math.Clamp(ScreenshotHistoryMaximumMiB, 0, 512),
                ElementMargin = Math.Clamp(ElementMargin, 0, 999),
                PolygonCornerRadius = Math.Clamp(PolygonCornerRadius, 0, 10),
                Magnification = Math.Clamp(Magnification, 1, 100),
                MagnifierGridColor = Color.TryParse(MagnifierGridColor, out var color) ? color.ToString() : "#80808080",
                BlockedWindowsKeys = BlockedWindowsKeys & CaptureBlockedWindowsKeys.All,
                ColorFormats = formats,
                MagnifierColorFormatId = SampleColorFormats.First(formats, MagnifierColorFormatId).Id,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                FrameDecoration = (FrameDecoration ?? new()).Normalize(),
                FrameRoundRadius = double.IsFinite(FrameRoundRadius) ? Math.Clamp(FrameRoundRadius, 0, 60) : 0,
            };
        }
    }
}
