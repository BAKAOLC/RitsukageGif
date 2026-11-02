using System.Runtime.InteropServices;

namespace RitsukageGif.Desktop
{
    internal sealed partial class MacAutomaticScrollInput
    {
        private static partial class Native
        {
            private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
            private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
            private static readonly nint GraphicsLibrary = NativeLibrary.Load(Graphics);
            private static readonly nint NumberKey = Key("kCGWindowNumber");
            private static readonly nint ProcessKey = Key("kCGWindowOwnerPID");
            private static readonly nint BoundsKey = Key("kCGWindowBounds");
            private static readonly nint AlphaKey = Key("kCGWindowAlpha");

            private static nint Key(string name)
            {
                return Marshal.ReadIntPtr(NativeLibrary.GetExport(GraphicsLibrary, name));
            }

            internal static bool TryGetPointer(out Point point)
            {
                point = default;
                var snapshot = CGEventCreate(0);
                if (snapshot == 0) return false;
                try
                {
                    point = CGEventGetLocation(snapshot);
                    return true;
                }
                finally
                {
                    CFRelease(snapshot);
                }
            }

            internal static bool ReadWindows(nint target, Point pointer, out int process, out nint front)
            {
                process = 0;
                front = 0;
                var windows = CGWindowListCopyWindowInfo(17, 0);
                if (windows == 0) return false;
                try
                {
                    var count = CFArrayGetCount(windows);
                    for (nint index = 0; index < count; index++)
                    {
                        var dictionary = CFArrayGetValueAtIndex(windows, index);
                        var numberValue = CFDictionaryGetValue(dictionary, NumberKey);
                        var processValue = CFDictionaryGetValue(dictionary, ProcessKey);
                        if (numberValue == 0 || processValue == 0 || !CfNumberGetInt(numberValue, 9, out var number) ||
                            !CfNumberGetInt(processValue, 9, out var owner)) continue;
                        if ((nint)(uint)number == target) process = owner;
                        var boundsValue = CFDictionaryGetValue(dictionary, BoundsKey);
                        if (front != 0 || boundsValue == 0 ||
                            !CgRectMakeWithDictionaryRepresentation(boundsValue, out var bounds)) continue;
                        var alphaValue = CFDictionaryGetValue(dictionary, AlphaKey);
                        if (alphaValue != 0 && CfNumberGetDouble(alphaValue, 13, out var alpha) && alpha <= 0) continue;
                        if (pointer.X >= bounds.Origin.X && pointer.Y >= bounds.Origin.Y &&
                            pointer.X < bounds.Origin.X + bounds.Size.Width &&
                            pointer.Y < bounds.Origin.Y + bounds.Size.Height)
                            front = (nint)(uint)number;
                    }

                    return process > 0;
                }
                finally
                {
                    CFRelease(windows);
                }
            }

            internal static bool PostScroll(int process, ScrollCaptureAxis axis, Point pointer)
            {
                var scroll = CGEventCreateScrollWheelEvent2(0, 1, 2,
                    axis == ScrollCaptureAxis.Vertical ? -1 : 0, axis == ScrollCaptureAxis.Horizontal ? -1 : 0, 0);
                if (scroll == 0) return false;
                try
                {
                    CGEventSetFlags(scroll, 0);
                    CGEventSetLocation(scroll, pointer);
                    CGEventPostToPid(process, scroll);
                    return true;
                }
                finally
                {
                    CFRelease(scroll);
                }
            }

            [LibraryImport(Graphics, EntryPoint = "CGPreflightPostEventAccess")]
            [return: MarshalAs(UnmanagedType.I1)]
            internal static partial bool CgPreflightPostEventAccess();

            [LibraryImport(Graphics, EntryPoint = "CGRequestPostEventAccess")]
            [return: MarshalAs(UnmanagedType.I1)]
            internal static partial bool CgRequestPostEventAccess();

            [LibraryImport(Graphics)]
            private static partial nint CGEventCreate(nint source);

            [LibraryImport(Graphics)]
            private static partial Point CGEventGetLocation(nint source);

            [LibraryImport(Graphics)]
            private static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint count, int first,
                int second, int third);

            [LibraryImport(Graphics)]
            private static partial void CGEventSetFlags(nint source, ulong flags);

            [LibraryImport(Graphics)]
            private static partial void CGEventSetLocation(nint source, Point location);

            [LibraryImport(Graphics)]
            private static partial void CGEventPostToPid(int process, nint source);

            [LibraryImport(Graphics)]
            private static partial nint CGWindowListCopyWindowInfo(uint options, uint relativeTo);

            [LibraryImport(Graphics, EntryPoint = "CGRectMakeWithDictionaryRepresentation")]
            [return: MarshalAs(UnmanagedType.I1)]
            private static partial bool
                CgRectMakeWithDictionaryRepresentation(nint dictionary, out Rectangle rectangle);

            [LibraryImport(Foundation)]
            private static partial nint CFArrayGetCount(nint array);

            [LibraryImport(Foundation)]
            private static partial nint CFArrayGetValueAtIndex(nint array, nint index);

            [LibraryImport(Foundation)]
            private static partial nint CFDictionaryGetValue(nint dictionary, nint key);

            [LibraryImport(Foundation, EntryPoint = "CFNumberGetValue")]
            [return: MarshalAs(UnmanagedType.I1)]
            private static partial bool CfNumberGetInt(nint number, int type, out int value);

            [LibraryImport(Foundation, EntryPoint = "CFNumberGetValue")]
            [return: MarshalAs(UnmanagedType.I1)]
            private static partial bool CfNumberGetDouble(nint number, int type, out double value);

            [LibraryImport(Foundation)]
            private static partial void CFRelease(nint value);

            [StructLayout(LayoutKind.Sequential)]
            internal struct Point
            {
                public double X, Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Size
            {
                public double Width, Height;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct Rectangle
            {
                public Point Origin;
                public Size Size;
            }
        }
    }
}
