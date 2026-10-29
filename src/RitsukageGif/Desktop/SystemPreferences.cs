namespace RitsukageGif.Desktop
{
    public sealed record SystemPreferences
    {
        public bool StartWithWindows { get; set; }
        public bool RunAsAdministrator { get; set; }
        public SystemTrayAction TraySingleClick { get; set; } = SystemTrayAction.ShowMainWindow;
        public SystemTrayAction TrayDoubleClick { get; set; }
        public SystemTrayAction TrayMiddleClick { get; set; }
        public UpdateChannel UpdateChannel { get; set; }

        public SystemPreferences Normalize()
        {
            return this with
            {
                TraySingleClick = Enum.IsDefined(TraySingleClick) ? TraySingleClick : SystemTrayAction.ShowMainWindow,
                TrayDoubleClick = Enum.IsDefined(TrayDoubleClick) ? TrayDoubleClick : SystemTrayAction.None,
                TrayMiddleClick = Enum.IsDefined(TrayMiddleClick) ? TrayMiddleClick : SystemTrayAction.None,
                UpdateChannel = Enum.IsDefined(UpdateChannel) ? UpdateChannel : UpdateChannel.Stable,
            };
        }
    }
}
