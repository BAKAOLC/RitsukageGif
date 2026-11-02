using System.ComponentModel;

namespace RitsukageGif.Desktop
{
    internal static class AutomaticScrollInputFactory
    {
        internal static AutomaticScrollConnection Probe(PixelRect region, WindowTarget? target,
            PortalAutomaticScrollBinding? portal = null)
        {
            try
            {
                if (region.IsEmpty) return new(null, "The capture region is empty.");
                if (OperatingSystem.IsWindows())
                {
                    var input = WindowsAutomaticScrollInput.Create(region, target);
                    return new(input, input is null ? "The selected capture window is unavailable." : null);
                }

                if (OperatingSystem.IsMacOS()) return MacAutomaticScrollInput.Create(region, target);
                if (OperatingSystem.IsLinux())
                {
                    if (!DesktopCapture.UsesSourceSelection) return X11AutomaticScrollInput.Create(region, target);
                    return portal is null
                        ? new(null, "Pointer control authorization is required for the selected capture source.", true)
                        : portal.IsCurrent()
                            ? new(new PortalAutomaticScrollInput(portal))
                            : new(null, portal.FaultReason() ?? "The authorized capture source changed.", true);
                }

                return new(null, "Automatic scrolling is not available on this platform.");
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
                                              NotSupportedException or IOException or Win32Exception)
            {
                return new(null, error.Message);
            }
            catch (TypeInitializationException error) when (error.InnerException is DllNotFoundException
                                                                or EntryPointNotFoundException)
            {
                return new(null, error.InnerException.Message);
            }
        }

        internal static async Task<AutomaticScrollConnection> ConnectAsync(PixelRect region, WindowTarget? target,
            Func<CancellationToken, Task<PortalAutomaticScrollBinding?>>? authorizePortal, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var connection = Probe(region, target);
            if (connection.Input is not null || !connection.CanRequestAccess) return connection;
            if (OperatingSystem.IsMacOS())
            {
                var granted = await Task.Run(MacAutomaticScrollInput.RequestPermission, token);
                token.ThrowIfCancellationRequested();
                if (!granted) return new(null, "Permission to post pointer events was denied.", true);
            }

            if (OperatingSystem.IsLinux() && DesktopCapture.UsesSourceSelection)
            {
                if (authorizePortal is null)
                    return new(null, "Pointer control authorization is unavailable for this capture source.");
                var binding = await authorizePortal(token);
                token.ThrowIfCancellationRequested();
                return binding is null
                    ? new(null, "Pointer control authorization was canceled or denied.", true)
                    : Probe(region, target, binding);
            }

            return Probe(region, target);
        }
    }
}
