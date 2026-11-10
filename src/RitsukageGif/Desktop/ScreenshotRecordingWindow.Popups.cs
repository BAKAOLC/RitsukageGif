using Avalonia;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotRecordingWindow
    {
        private IDisposable? _nativePopupSubscription;

        private void InitializeNativePopupOwnership()
        {
            if (!OperatingSystem.IsLinux()) return;
            _nativePopupSubscription = IsVisibleProperty.Changed.AddClassHandler<PopupRoot>((popup, _) =>
            {
                if (_closed || !popup.IsVisible) return;
                var owner = popup.ParentTopLevel;
                while (owner is PopupRoot parent) owner = parent.ParentTopLevel;
                if (owner != this && owner != _sourceViewport && !Annotations.OwnsNativeExclusionWindow(owner))
                    return;
                if (popup.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } child ||
                    owner.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } root) return;
                if (X11Capture.SetPopupTransient(child.Handle, root.Handle)) return;
                HideRecorderPanels();
                Annotations.HideFlyouts();
                LogHub.GetLogger("Desktop.ScreenshotRecording").ZLogWarning(
                    $"Recording popup capture ownership could not be established.");
            });
        }
    }
}
