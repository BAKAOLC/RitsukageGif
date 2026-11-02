using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal static partial class PortableGlobalMouseObserver
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        private static unsafe void RunMac(Action<uint, GlobalMouseHookData, KeyModifiers> input, Action ready,
            CancellationToken cancellationToken)
        {
            if (CGPreflightListenEventAccess() == 0)
                throw new UnauthorizedAccessException(
                    "Global mouse monitoring requires macOS Input Monitoring permission. Enable it in System Settings > Privacy & Security > Input Monitoring.");
            var state = new PortableMouseMacSession(input, cancellationToken);
            var handle = GCHandle.Alloc(state);
            nint source = 0;
            nint mode = 0;
            try
            {
                const ulong events = (1UL << 1) | (1UL << 2) | (1UL << 3) | (1UL << 4) | (1UL << 5) |
                                     (1UL << 6) | (1UL << 7) | (1UL << 25) | (1UL << 26) | (1UL << 27);
                state.Tap = CGEventTapCreate(1, 0, 1, events, &OnMacMouse, GCHandle.ToIntPtr(handle));
                if (state.Tap == 0)
                    throw new UnauthorizedAccessException(
                        "macOS could not create a passive global mouse event tap. Check Input Monitoring permission.");
                source = CFMachPortCreateRunLoopSource(0, state.Tap, 0);
                mode = CfStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
                if (source == 0 || mode == 0)
                    throw new InvalidOperationException(
                        "macOS could not create the global mouse observation run loop source.");
                state.RunLoop = CFRunLoopGetCurrent();
                CFRunLoopAddSource(state.RunLoop, source, mode);
                CGEventTapEnable(state.Tap, 1);
                if (CGEventTapIsEnabled(state.Tap) == 0)
                    throw new UnauthorizedAccessException("macOS global mouse event monitoring is disabled.");
                using var cancellation = cancellationToken.Register(() => CFRunLoopStop(state.RunLoop));
                if (cancellationToken.IsCancellationRequested) return;
                ready();
                while (!cancellationToken.IsCancellationRequested && state.Failure is null)
                {
                    var result = CFRunLoopRunInMode(mode, 0.25, 1);
                    if (result is 1 or 2 && !cancellationToken.IsCancellationRequested && state.Failure is null)
                        throw new IOException("macOS global mouse observation run loop stopped unexpectedly.");
                    if (!cancellationToken.IsCancellationRequested && state.Failure is null &&
                        CGEventTapIsEnabled(state.Tap) == 0) RecoverMacTap(state);
                }

                if (state.Failure is { } failure) throw failure;
            }
            finally
            {
                if (state.Tap != 0)
                {
                    CGEventTapEnable(state.Tap, 0);
                    CFMachPortInvalidate(state.Tap);
                }

                if (state.RunLoop != 0 && source != 0 && mode != 0)
                    CFRunLoopRemoveSource(state.RunLoop, source, mode);
                if (source != 0) CFRelease(source);
                if (state.Tap != 0) CFRelease(state.Tap);
                if (mode != 0) CFRelease(mode);
                handle.Free();
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static nint OnMacMouse(nint proxy, uint type, nint input, nint context)
        {
            var state = (PortableMouseMacSession)GCHandle.FromIntPtr(context).Target!;
            try
            {
                if (state.CancellationToken.IsCancellationRequested) return input;
                if (type is 0xFFFFFFFE or 0xFFFFFFFF)
                {
                    RecoverMacTap(state);
                    return input;
                }

                var position = CGEventGetLocation(input);
                var button = type switch
                {
                    1 or 2 => 1,
                    3 or 4 => 2,
                    25 or 26 => CGEventGetIntegerValueField(input, 3) switch
                    {
                        2 => 3,
                        3 => 4,
                        4 => 5,
                        _ => 0,
                    },
                    _ => 0,
                };
                var message = type is 5 or 6 or 7 or 27 ? 0x0200u : MouseMessage(button, type is 1 or 3 or 25);
                if (message == 0) return input;
                if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
                    position.X is < int.MinValue or > int.MaxValue || position.Y is < int.MinValue or > int.MaxValue)
                {
                    state.Input(0x001F, default, KeyModifiers.None);
                    return input;
                }

                var flags = CGEventGetFlags(input);
                state.Input(message, new()
                {
                    X = (int)Math.Round(position.X), Y = (int)Math.Round(position.Y),
                    MouseData = ExtraButtonData(button),
                }, MacModifiers(flags));
            }
            catch (Exception error)
            {
                state.Failure = error;
                CFRunLoopStop(state.RunLoop);
            }

            return input;
        }

        private static void RecoverMacTap(PortableMouseMacSession state)
        {
            state.Input(0x001F, default, KeyModifiers.None);
            CGEventTapEnable(state.Tap, 1);
            if (CGEventTapIsEnabled(state.Tap) == 0)
                throw new UnauthorizedAccessException(
                    "macOS global mouse monitoring was disabled. Check Input Monitoring permission.");
        }

        private static KeyModifiers MacModifiers(ulong flags)
        {
            var result = KeyModifiers.None;
            if ((flags & (1UL << 17)) != 0) result |= KeyModifiers.Shift;
            if ((flags & (1UL << 18)) != 0) result |= KeyModifiers.Control;
            if ((flags & (1UL << 19)) != 0) result |= KeyModifiers.Alt;
            if ((flags & (1UL << 20)) != 0) result |= KeyModifiers.Meta;
            return result;
        }

        [LibraryImport(CoreGraphics)]
        private static partial byte CGPreflightListenEventAccess();

        [LibraryImport(CoreGraphics)]
        private static unsafe partial nint CGEventTapCreate(uint tap, uint place, uint options, ulong events,
            delegate* unmanaged[Cdecl]<nint, uint, nint, nint, nint> callback, nint context);

        [LibraryImport(CoreGraphics)]
        private static partial void CGEventTapEnable(nint tap, byte enabled);

        [LibraryImport(CoreGraphics)]
        private static partial byte CGEventTapIsEnabled(nint tap);

        [LibraryImport(CoreGraphics)]
        private static partial PortableMouseMacPoint CGEventGetLocation(nint input);

        [LibraryImport(CoreGraphics)]
        private static partial ulong CGEventGetFlags(nint input);

        [LibraryImport(CoreGraphics)]
        private static partial long CGEventGetIntegerValueField(nint input, uint field);

        [LibraryImport(CoreFoundation)]
        private static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

        [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint CfStringCreateWithCString(nint allocator, string text, uint encoding);

        [LibraryImport(CoreFoundation)]
        private static partial nint CFRunLoopGetCurrent();

        [LibraryImport(CoreFoundation)]
        private static partial void CFRunLoopAddSource(nint runLoop, nint source, nint mode);

        [LibraryImport(CoreFoundation)]
        private static partial void CFRunLoopRemoveSource(nint runLoop, nint source, nint mode);

        [LibraryImport(CoreFoundation)]
        private static partial int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSourceHandled);

        [LibraryImport(CoreFoundation)]
        private static partial void CFRunLoopStop(nint runLoop);

        [LibraryImport(CoreFoundation)]
        private static partial void CFMachPortInvalidate(nint port);

        [LibraryImport(CoreFoundation)]
        private static partial void CFRelease(nint value);
    }
}
