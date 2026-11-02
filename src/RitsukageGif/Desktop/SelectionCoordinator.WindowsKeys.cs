using Avalonia;
using Avalonia.Controls.Primitives;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionCoordinator
    {
        private CaptureWindowsKeyGuard? _windowsKeyGuard;
        private readonly List<Task> _windowsKeyGuardStops = [];
        private IDisposable? _windowsKeyPopupSubscription;
        private HashSet<PopupRoot>? _windowsKeyPopups;

        private async Task StartWindowsKeyGuardAsync()
        {
            if (_finished || _scrolling || CapturePreferences.BlockedWindowsKeys == CaptureBlockedWindowsKeys.None ||
                !OperatingSystem.IsWindows()) return;
            _windowsKeyPopupSubscription ??=
                Visual.IsVisibleProperty.Changed.AddClassHandler<PopupRoot>((popup, _) =>
                {
                    if (popup.IsVisible && IsCapturePopup(popup)) (_windowsKeyPopups ??= []).Add(popup);
                    else _windowsKeyPopups?.Remove(popup);
                    RefreshWindowsKeyGuard();
                });
            var guard = await CaptureWindowsKeyGuard.CreateAsync(CapturePreferences.BlockedWindowsKeys,
                CaptureWindowsKeyHandles(), _cancel.Token);
            _windowsKeyGuard = guard;
            if (_finished || _scrolling) StopWindowsKeyGuard();
            else RefreshWindowsKeyGuard();
        }

        private void RefreshWindowsKeyGuard()
        {
            _windowsKeyGuard?.UpdateAllowedWindows(CaptureWindowsKeyHandles());
        }

        private nint[] CaptureWindowsKeyHandles()
        {
            if (_finished || _scrolling || _selectionDialogDepth != 0) return [];
            return
            [
                .. _windows.Where(overlay => overlay.IsVisible)
                    .Select(overlay => overlay.TryGetPlatformHandle()?.Handle ?? 0)
                    .Concat(_windowsKeyPopups?.Where(popup => popup.IsVisible && IsCapturePopup(popup))
                        .Select(popup => popup.TryGetPlatformHandle()?.Handle ?? 0) ?? [])
                    .Where(handle => handle != 0),
            ];
        }

        private void StopWindowsKeyGuard()
        {
            if (_windowsKeyGuard is not { } guard) return;
            _windowsKeyGuard = null;
            guard.Dispose();
            _windowsKeyGuardStops.Add(guard.Completion);
        }

        private void DisposeWindowsKeyGuard()
        {
            StopWindowsKeyGuard();
            _windowsKeyPopupSubscription?.Dispose();
            _windowsKeyPopupSubscription = null;
            _windowsKeyPopups?.Clear();
        }

        private async Task WaitForWindowsKeyGuardsAsync()
        {
            try
            {
                await Task.WhenAll(_windowsKeyGuardStops);
            }
            catch (Exception error)
            {
                SelectionLogger.ZLogError(error,
                    $"Capture Windows-key guard shutdown failed. SelectionId={_selectionId}");
            }

            _windowsKeyGuardStops.Clear();
        }

        private bool IsCapturePopup(PopupRoot popup)
        {
            var owner = popup.ParentTopLevel;
            while (owner is PopupRoot parent) owner = parent.ParentTopLevel;
            return owner is OverlayWindow overlay && _windows.Contains(overlay);
        }
    }
}
