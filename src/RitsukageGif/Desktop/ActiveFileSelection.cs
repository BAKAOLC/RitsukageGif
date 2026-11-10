namespace RitsukageGif.Desktop
{
    internal static class ActiveFileSelection
    {
        internal static bool IsSupported => OperatingSystem.IsWindows() ||
                                            (OperatingSystem.IsMacOS() && File.Exists("/usr/bin/osascript"));

        private static string? UnavailableReason => IsSupported
            ? null
            : UiText.Get("GlobalActionFileSelectionUnavailable");

        internal static Task<IReadOnlyList<string>> ReadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows()) return WindowsActiveFileSelection.ReadAsync(token);
            if (OperatingSystem.IsMacOS() && IsSupported) return MacActiveFileSelection.ReadAsync(token);
            throw new NotSupportedException(UnavailableReason);
        }
    }
}
