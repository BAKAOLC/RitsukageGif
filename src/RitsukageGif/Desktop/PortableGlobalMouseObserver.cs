using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class PortableGlobalMouseObserver
    {
        internal static void Run(Action<uint, GlobalMouseHookData, KeyModifiers> input, Action ready,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsMacOS()) RunMac(input, ready, cancellationToken);
            else if (OperatingSystem.IsLinux() && !DesktopCapture.UsesSourceSelection)
                RunX11(input, ready, cancellationToken);
            else
                throw new PlatformNotSupportedException(
                    "Passive global mouse observation is unavailable on this desktop.");
        }

        private static uint MouseMessage(int button, bool pressed)
        {
            return button switch
            {
                1 => pressed ? 0x0201u : 0x0202u,
                2 => pressed ? 0x0204u : 0x0205u,
                3 => pressed ? 0x0207u : 0x0208u,
                4 or 5 => pressed ? 0x020Bu : 0x020Cu,
                _ => 0,
            };
        }

        private static uint ExtraButtonData(int button)
        {
            return button switch { 4 => 1u << 16, 5 => 2u << 16, _ => 0 };
        }
    }
}
