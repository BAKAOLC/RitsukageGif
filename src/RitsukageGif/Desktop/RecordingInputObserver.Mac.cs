using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingInputObserver
    {
        private const string RecordingCoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

        private const string RecordingCoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private nint _macTap;

        private nint _macRunLoop;
        private ulong _macTime;
        private long _macTimestamp;

        private unsafe void RunMac()
        {
            if (CGPreflightListenEventAccess() == 0)
                throw new UnauthorizedAccessException(
                    "Recording input observation requires macOS Input Monitoring permission.");
            var state = GCHandle.Alloc(this);
            nint source = 0, mode = 0, stopSource = 0;
            try
            {
                if (MachTimebase(out var timebase) != 0 || timebase.Denominator == 0)
                    throw new IOException("macOS could not read the recording input clock.");
                var before = Stopwatch.GetTimestamp();
                _macTime = (ulong)((UInt128)MachAbsoluteTime() * timebase.Numerator / timebase.Denominator);
                _macTimestamp = before + (Stopwatch.GetTimestamp() - before) / 2;
                var mask = 1UL << 12; // FlagsChanged supplies modifiers for mouse-only recordings as well.
                if (_recordKeyboard) mask |= (1UL << 10) | (1UL << 11);
                if (_recordMouse)
                    mask |= (1UL << 1) | (1UL << 2) | (1UL << 3) | (1UL << 4) |
                            (1UL << 22) | (1UL << 25) | (1UL << 26);
                _macTap = CGEventTapCreate(1, 0, 1, mask, &RecordingMacInput, GCHandle.ToIntPtr(state));
                if (_macTap == 0)
                    throw new UnauthorizedAccessException(
                        "macOS could not create a passive recording input event tap.");
                source = CFMachPortCreateRunLoopSource(0, _macTap, 0);
                mode = RecordingCfString(0, "kCFRunLoopDefaultMode", 0x08000100);
                if (source == 0 || mode == 0)
                    throw new IOException("macOS could not create the recording input run loop source.");
                _macRunLoop = CFRunLoopGetCurrent();
                CFRunLoopAddSource(_macRunLoop, source, mode);
                var stopContext = new RecordingRunLoopSourceContext
                {
                    Info = GCHandle.ToIntPtr(state),
                    Perform = (nint)(delegate* unmanaged[Cdecl]<nint, void>)&StopRecordingMacRunLoop,
                };
                stopSource = CFRunLoopSourceCreate(0, 0, ref stopContext);
                if (stopSource == 0) throw new IOException("macOS could not create the recording cancellation source.");
                CFRunLoopAddSource(_macRunLoop, stopSource, mode);
                CGEventTapEnable(_macTap, 1);
                if (CGEventTapIsEnabled(_macTap) == 0)
                    throw new UnauthorizedAccessException("macOS recording input monitoring is disabled.");
                var cancellationSource = stopSource;
                using var cancellation = _stop.Token.Register(() =>
                {
                    CFRunLoopSourceSignal(cancellationSource);
                    CFRunLoopWakeUp(_macRunLoop);
                });
                _stop.Token.ThrowIfCancellationRequested();
                _ready.TrySetResult();
                CFRunLoopRun();
                if (!_stop.IsCancellationRequested)
                    throw new IOException("macOS recording input observation stopped unexpectedly.");
            }
            finally
            {
                if (_macTap != 0)
                {
                    CGEventTapEnable(_macTap, 0);
                    CFMachPortInvalidate(_macTap);
                }

                if (_macRunLoop != 0 && source != 0 && mode != 0) CFRunLoopRemoveSource(_macRunLoop, source, mode);
                if (_macRunLoop != 0 && stopSource != 0 && mode != 0)
                    CFRunLoopRemoveSource(_macRunLoop, stopSource, mode);
                if (stopSource != 0) CFRelease(stopSource);
                if (source != 0) CFRelease(source);
                if (_macTap != 0) CFRelease(_macTap);
                if (mode != 0) CFRelease(mode);
                _macTap = _macRunLoop = 0;
                state.Free();
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void StopRecordingMacRunLoop(nint context)
        {
            var observer = (RecordingInputObserver)GCHandle.FromIntPtr(context).Target!;
            CFRunLoopStop(observer._macRunLoop);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static nint RecordingMacInput(nint proxy, uint type, nint input, nint context)
        {
            var observer = (RecordingInputObserver)GCHandle.FromIntPtr(context).Target!;
            try
            {
                if (observer._stop.IsCancellationRequested) return input;
                if (type is 0xFFFFFFFE or 0xFFFFFFFF)
                {
                    CGEventTapEnable(observer._macTap, 1);
                    if (CGEventTapIsEnabled(observer._macTap) == 0)
                        observer.Fail(
                            new UnauthorizedAccessException("macOS recording input monitoring was disabled."));
                    return input;
                }

                var timestamp = observer._macTimestamp +
                                (long)(((Int128)CGEventGetTimestamp(input) - observer._macTime) * Stopwatch.Frequency /
                                       1_000_000_000);
                var flags = CGEventGetFlags(input);
                var modifiers = ((flags & (1UL << 17)) != 0 ? KeyModifiers.Shift : 0) |
                                ((flags & (1UL << 18)) != 0 ? KeyModifiers.Control : 0) |
                                ((flags & (1UL << 19)) != 0 ? KeyModifiers.Alt : 0) |
                                ((flags & (1UL << 20)) != 0 ? KeyModifiers.Meta : 0);
                if (type is 10 or 11 or 12)
                {
                    if (!observer._recordKeyboard) return input;
                    var code = (uint)CGEventGetIntegerValueField(input, 9);
                    var down = type == 10 || (type == 12 && CGEventSourceKeyState(1, (ushort)code) != 0);
                    observer.Emit(new(timestamp, down ? RecordingInputEventKind.KeyDown : RecordingInputEventKind.KeyUp,
                        MacKey(code), modifiers, NativeCode: code, NativeScanCode: code));
                    return input;
                }

                var location = CGEventGetLocation(input);
                if (type == 22)
                {
                    var vertical = CGEventGetIntegerValueField(input, 11);
                    var horizontal = CGEventGetIntegerValueField(input, 12);
                    if (vertical != 0)
                        observer.Emit(new(timestamp, RecordingInputEventKind.Wheel,
                            Modifiers: modifiers, X: location.X, Y: location.Y,
                            WheelDelta: (int)Math.Clamp(vertical * 120, int.MinValue, int.MaxValue),
                            NativeCode: 0x020A));
                    if (horizontal != 0)
                        observer.Emit(new(timestamp, RecordingInputEventKind.Wheel,
                            Modifiers: modifiers, X: location.X, Y: location.Y,
                            WheelDelta: (int)Math.Clamp(horizontal * 120, int.MinValue, int.MaxValue),
                            NativeCode: 0x020E));
                }
                else
                {
                    var button = type switch
                    {
                        1 or 2 => 1, 3 or 4 => 2,
                        25 or 26 => CGEventGetIntegerValueField(input, 3) switch { 2 => 4, 3 => 8, 4 => 16, _ => 0 },
                        _ => 0,
                    };
                    if (button != 0)
                        observer.Emit(new(timestamp,
                            type is 1 or 3 or 25
                                ? RecordingInputEventKind.PointerDown
                                : RecordingInputEventKind.PointerUp,
                            Modifiers: modifiers, Button: button, X: location.X, Y: location.Y));
                }
            }
            catch (Exception error)
            {
                observer.Fail(error);
            }

            return input;
        }

        [LibraryImport(RecordingCoreGraphics)]
        private static partial byte CGPreflightListenEventAccess();

        [LibraryImport(RecordingCoreGraphics)]
        private static unsafe partial nint CGEventTapCreate(uint tap, uint place, uint options, ulong mask,
            delegate* unmanaged[Cdecl]<nint, uint, nint, nint, nint> callback, nint context);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial void CGEventTapEnable(nint tap, byte enabled);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial byte CGEventTapIsEnabled(nint tap);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial ulong CGEventGetTimestamp(nint input);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial ulong CGEventGetFlags(nint input);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial long CGEventGetIntegerValueField(nint input, uint field);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial RecordingInputPoint CGEventGetLocation(nint input);

        [LibraryImport(RecordingCoreGraphics)]
        private static partial byte CGEventSourceKeyState(int source, ushort key);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial nint CFMachPortCreateRunLoopSource(nint allocator, nint port, nint order);

        [LibraryImport(RecordingCoreFoundation, EntryPoint = "CFStringCreateWithCString",
            StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint RecordingCfString(nint allocator, string text, uint encoding);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial nint CFRunLoopGetCurrent();

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopAddSource(nint loop, nint source, nint mode);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopRemoveSource(nint loop, nint source, nint mode);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopRun();

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopStop(nint loop);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopWakeUp(nint loop);

        // ReSharper disable once InconsistentNaming
        [LibraryImport(RecordingCoreFoundation)]
        private static partial nint CFRunLoopSourceCreate(nint allocator, nint order,
            ref RecordingRunLoopSourceContext context);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRunLoopSourceSignal(nint source);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFMachPortInvalidate(nint port);

        [LibraryImport(RecordingCoreFoundation)]
        private static partial void CFRelease(nint value);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mach_absolute_time")]
        private static partial ulong MachAbsoluteTime();

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mach_timebase_info")]
        private static partial int MachTimebase(out RecordingMachTimebase timebase);
    }
}
