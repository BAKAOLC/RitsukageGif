using Avalonia;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedDocumentWindow
    {
        internal double AutomationScrollX => ContentScrollOffset().X * RenderScaling;
        internal double AutomationScrollY => ContentScrollOffset().Y * RenderScaling;

        // ReSharper disable once ConvertToAutoProperty
        internal double AutomationZoom => _zoom;

        // ReSharper disable once ConvertToAutoProperty
        internal bool AutomationThumbnail => _isThumbnail;
        internal bool AutomationAnnotationsVisible => !IsMarkHidden;
        internal bool AutomationCanHideAnnotations => !IsMarkEditing;
        internal bool? AutomationSelectable => Document is PinnedTextDocument or PinnedHtmlDocument ? Selectable : null;
        internal bool? AutomationIgnoreFormat => Document is PinnedHtmlDocument ? IgnoreFormat : null;

        internal void SetAutomationScroll(double? x, double? y)
        {
            if (_busy || IsInteractionLocked || IsMarkEditing)
                throw new InvalidOperationException("The pin cannot scroll while locked, busy or editing.");
            var offset = ContentScrollOffset();
            SetContentScrollOffset(new(x is { } left ? left / RenderScaling : offset.X,
                y is { } top ? top / RenderScaling : offset.Y));
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetAutomationThumbnailRegion(PixelRect region)
        {
            if (IsInteractionLocked || IsMarkEditing)
                throw new InvalidOperationException("The pin cannot select a thumbnail region in its current state.");
            if (region.IsEmpty) throw new ArgumentOutOfRangeException(nameof(region));
            EnsureDocumentSceneLayout();
            var bounds = new Rect(region.X / RenderScaling, region.Y / RenderScaling,
                region.Width / RenderScaling, region.Height / RenderScaling).Intersect(new(MeasureDocumentScene()));
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
            BeginDocumentThumbnail(bounds.Center, bounds);
        }

        internal void SetAutomationTextOptions(bool? selectable, bool? ignoreFormat)
        {
            if (selectable is { } value) SetSelectable(value);
            if (ignoreFormat is { } ignore) SetIgnoreFormat(ignore);
        }

        internal void SetAutomationAnnotationsVisible(bool value)
        {
            if (IsMarkEditing) throw new InvalidOperationException("Finish interactive annotation editing first.");
            SetMarkHidden(!value);
        }

        internal void SetAutomationTopmost(bool value)
        {
            Topmost = value;
            RefreshPreferencePresentation();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetAutomationZoom(double value)
        {
            if (IsInteractionLocked) throw new InvalidOperationException("The pin is locked.");
            SetZoom(value);
        }

        internal void FitAutomation(int width, int height)
        {
            if (IsInteractionLocked) throw new InvalidOperationException("The pin is locked.");
            Width = width / RenderScaling;
            Height = height / RenderScaling;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
