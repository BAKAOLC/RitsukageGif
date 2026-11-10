namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private void RefreshShapeSourceSize()
        {
            var size = _bitmap.Size;
            Presentation.SetShapeSourceSize(new(size.Width / OriginalCaptureScaling,
                size.Height / OriginalCaptureScaling));
        }

        private void RefreshWindowShadow()
        {
            RefreshShapeSourceSize();
            Presentation.RefreshShade(IsLocked, IsMouseThrough, _isThumbnail, Group, GroupsProvider());
        }
    }
}
