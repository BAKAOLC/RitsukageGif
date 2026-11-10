namespace RitsukageGif.Desktop
{
    public sealed record SystemPreferences
    {
        public bool StartWithWindows { get; set; }
        public bool StartupHidden { get; set; } = true;
        public bool RunAsAdministrator { get; set; }
        public bool IgnoreCopyMacros { get; set; } = true;
        public ExplorerContextMenuMode ExplorerContextMenu { get; set; }
        public DesktopToolbarPreferences DesktopToolbar { get; set; } = new();
        public SystemTrayAction TraySingleClick { get; set; } = SystemTrayAction.Screenshot;
        public SystemTrayAction TrayDoubleClick { get; set; }
        public SystemTrayAction TrayMiddleClick { get; set; } = SystemTrayAction.ScreenshotAndPin;
        public UpdateChannel UpdateChannel { get; set; }

        public SystemPreferences Normalize()
        {
            return this with
            {
                ExplorerContextMenu = Enum.IsDefined(ExplorerContextMenu)
                    ? ExplorerContextMenu
                    : ExplorerContextMenuMode.Disabled,
                // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
                DesktopToolbar = (DesktopToolbar ?? new()).Normalize(),
                TraySingleClick = Enum.IsDefined(TraySingleClick) ? TraySingleClick : SystemTrayAction.Screenshot,
                TrayDoubleClick = Enum.IsDefined(TrayDoubleClick) ? TrayDoubleClick : SystemTrayAction.None,
                TrayMiddleClick = Enum.IsDefined(TrayMiddleClick) ? TrayMiddleClick : SystemTrayAction.ScreenshotAndPin,
                UpdateChannel = Enum.IsDefined(UpdateChannel) ? UpdateChannel : UpdateChannel.Stable,
            };
        }
    }
}
