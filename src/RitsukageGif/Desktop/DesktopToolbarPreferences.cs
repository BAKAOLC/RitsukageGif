namespace RitsukageGif.Desktop
{
    public sealed record DesktopToolbarPreferences
    {
        public DesktopToolbarMode Mode { get; set; } = DesktopToolbarMode.Disabled;

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public string[] Primary { get; set; } =
        [
            "screenshot", "pin",
            "ocr", "scroll",
            "record",
        ];

        // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
        public string[] More { get; set; } =
        [
            "delayed", "translate", "table",
            "formula",
        ];

        public DateTimeOffset? HiddenUntil { get; set; }
        public int? Left { get; set; }
        public int? Top { get; set; }

        internal static bool IsSupported(string kind)
        {
            return kind is
                "screenshot" or "pin" or "delayed" or
                "ocr" or "scroll" or
                "record" or "translate" or
                "table" or "formula";
        }

        public DesktopToolbarPreferences Normalize()
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            var primary = (Primary ?? []).Where(IsSupported).Distinct().ToArray();
            return this with
            {
                Mode = Enum.IsDefined(Mode) ? Mode : DesktopToolbarMode.Disabled,
                Primary = primary,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                More = [.. (More ?? []).Where(IsSupported).Except(primary).Distinct()],
            };
        }
    }
}
