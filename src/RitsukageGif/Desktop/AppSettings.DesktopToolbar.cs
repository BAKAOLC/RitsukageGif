namespace RitsukageGif.Desktop
{
    public sealed partial record AppSettings
    {
        internal static bool Same(DesktopToolbarPreferences left, DesktopToolbarPreferences right)
        {
            return left.Mode == right.Mode && left.HiddenUntil == right.HiddenUntil &&
                   left.Left == right.Left && left.Top == right.Top &&
                   left.Primary.SequenceEqual(right.Primary) && left.More.SequenceEqual(right.More);
        }

        private static DesktopToolbarPreferences Merge(DesktopToolbarPreferences current,
            DesktopToolbarPreferences baseline, DesktopToolbarPreferences edited)
        {
            return new()
            {
                Mode = baseline.Mode == edited.Mode ? current.Mode : edited.Mode,
                HiddenUntil = baseline.HiddenUntil == edited.HiddenUntil ? current.HiddenUntil : edited.HiddenUntil,
                Left = baseline.Left == edited.Left ? current.Left : edited.Left,
                Top = baseline.Top == edited.Top ? current.Top : edited.Top,
                Primary = baseline.Primary.SequenceEqual(edited.Primary) ? [.. current.Primary] : [.. edited.Primary],
                More = baseline.More.SequenceEqual(edited.More) ? [.. current.More] : [.. edited.More],
            };
        }
    }
}
