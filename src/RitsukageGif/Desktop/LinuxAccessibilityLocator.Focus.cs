using System.Globalization;

namespace RitsukageGif.Desktop
{
    internal static partial class LinuxAccessibilityLocator
    {
        internal static async Task<string?> ReadFocusedIdentityAsync(uint processId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (processId == 0 || processId == Environment.ProcessId) return null;
            var result = await RunAsync(["--focus", processId.ToString(CultureInfo.InvariantCulture)], 750, token)
                .ConfigureAwait(false);
            if (result is not { Code: 0 }) return null;
            var parts = result.Value.Output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts is ["FOCUS", { Length: > 1 } bus, { Length: > 1 } path] &&
                   bus[0] == ':' && path[0] == '/'
                ? bus + " " + path
                : null;
        }
    }
}
