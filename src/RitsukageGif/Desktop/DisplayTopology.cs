namespace RitsukageGif.Desktop
{
    internal sealed class DisplayTopology
    {
        public long Version { get; }
        public IReadOnlyList<PixelRect> Monitors { get; }

        public DisplayTopology(IEnumerable<PixelRect> monitors, long version = 0)
        {
            Version = version;
            Monitors = Array.AsReadOnly(monitors.Where(monitor => !monitor.IsEmpty).Distinct()
                .OrderBy(monitor => monitor.X).ThenBy(monitor => monitor.Y)
                .ThenBy(monitor => monitor.Width).ThenBy(monitor => monitor.Height).ToArray());
        }

        public bool Covers(PixelRect region)
        {
            foreach (var monitor in Monitors)
                if (!monitor.Intersect(region).IsEmpty)
                    return true;
            return false;
        }
    }
}
