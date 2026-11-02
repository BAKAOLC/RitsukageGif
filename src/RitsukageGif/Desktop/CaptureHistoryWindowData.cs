namespace RitsukageGif.Desktop
{
    internal sealed class CaptureHistoryWindowData
    {
        public string Process { get; set; } = "";
        public string Title { get; set; } = "";
        public PixelRect Bounds { get; set; }
        public PixelRect Client { get; set; }

        internal static CaptureHistoryWindowData FromWindow(WindowTarget window)
        {
            return new()
                { Process = window.Process, Title = window.Title, Bounds = window.Bounds, Client = window.Client };
        }

        internal WindowTarget ToWindow()
        {
            return new(0, Process, Title, Bounds, Client);
        }
    }
}
