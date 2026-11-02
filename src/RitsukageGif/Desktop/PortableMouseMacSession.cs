using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed class PortableMouseMacSession(
        Action<uint, GlobalMouseHookData, KeyModifiers> input,
        CancellationToken cancellationToken)
    {
        internal Action<uint, GlobalMouseHookData, KeyModifiers> Input { get; } = input;
        internal CancellationToken CancellationToken { get; } = cancellationToken;
        internal nint Tap { get; set; }
        internal nint RunLoop { get; set; }
        internal Exception? Failure { get; set; }
    }
}
