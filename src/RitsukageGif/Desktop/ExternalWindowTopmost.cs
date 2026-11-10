namespace RitsukageGif.Desktop
{
    internal static class ExternalWindowTopmost
    {
        internal static bool IsSupported => OperatingSystem.IsWindows() ||
                                            (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection &&
                                             X11WindowSystem.SupportsExternalTopmost());

        private static string? UnavailableReason => IsSupported ? null : UiText.Get("GlobalActionTopmostUnavailable");

        internal static Task<bool> ToggleAtPointerAsync(Avalonia.PixelPoint point, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) return Task.FromResult(WindowsExternalWindowTopmost.Toggle(point));
            if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                return X11WindowSystem.ToggleTopmostAtPointAsync(point, token);
            throw new NotSupportedException(UnavailableReason);
        }
    }
}
