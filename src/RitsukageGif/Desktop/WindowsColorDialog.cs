using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    internal static unsafe partial class WindowsColorDialog
    {
        private const int AlphaControl = 0x7101;

        internal static Color? Choose(nint owner, Color initial, bool alpha)
        {
            Span<uint> custom = stackalloc uint[16];
            custom.Clear();
            var label = UiText.Get("AppearanceOpacity");
            fixed (uint* colors = custom)
            fixed (char* text = label)
            {
                var state = new AlphaState
                    { Alpha = initial.A, InitialPercent = (int)Math.Round(initial.A * 100d / 255), Label = text };
                var request = new ChooseColorData
                {
                    Size = (uint)sizeof(ChooseColorData), Owner = owner,
                    Color = (uint)(initial.R | (initial.G << 8) | (initial.B << 16)), CustomColors = colors,
                    Flags = 1 | 2 | (alpha ? 0x10u : 0), Data = (nint)(&state), HookProcedure = alpha ? &Hook : null,
                };
                if (ChooseColor(&request) == 0)
                {
                    var error = CommDlgExtendedError();
                    if (error != 0) throw new InvalidOperationException(UiText.Get("AppearanceColorDialogFailed"));
                    return null;
                }

                if (state.InitializationFailed)
                    throw new InvalidOperationException(UiText.Get("AppearanceColorDialogFailed"));
                return Color.FromArgb(alpha ? state.Alpha : initial.A, (byte)request.Color,
                    (byte)(request.Color >> 8), (byte)(request.Color >> 16));
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static nuint Hook(nint window, uint message, nuint wParam, nint lParam)
        {
            try
            {
                return HandleHook(window, message, wParam, lParam);
            }
            catch (Exception error)
            {
                Trace.TraceWarning("Native color dialog hook failed: {0}", error.Message);
                return 0;
            }
        }

        private static nuint HandleHook(nint window, uint message, nuint wParam, nint lParam)
        {
            switch (message)
            {
                case 0x110:
                {
                    var request = (ChooseColorData*)lParam;
                    var state = (AlphaState*)request->Data;
                    state->InitializationFailed = true;
                    Marshal.SetLastPInvokeError(0);
                    if ((SetWindowLongPtr(window, -21, request->Data) == 0 && Marshal.GetLastPInvokeError() != 0) ||
                        GetClientRect(window, out var client) == 0 || GetWindowRect(window, out var bounds) == 0)
                        throw new InvalidOperationException(UiText.Get("AppearanceColorDialogFailed"));
                    var dpi = GetDpiForWindow(window);
                    var scale = (dpi == 0 ? 96 : dpi) / 96d;
                    var height = (int)Math.Round(34 * scale);
                    if (SetWindowPos(window, 0, 0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top + height,
                            0x16) == 0)
                        throw new InvalidOperationException(UiText.Get("AppearanceColorDialogFailed"));
                    var y = client.Bottom + (int)Math.Round(4 * scale);
                    var label = CreateWindowEx(0, "STATIC", new(state->Label), 0x50000000,
                        (int)(12 * scale), y, (int)(160 * scale), (int)(24 * scale), window, 0, 0, 0);
                    var input = CreateWindowEx(0x200, "EDIT",
                        state->InitialPercent.ToString(CultureInfo.InvariantCulture),
                        0x50012080, (int)(176 * scale), y, (int)(62 * scale), (int)(24 * scale), window, AlphaControl,
                        0,
                        0);
                    if (label == 0 || input == 0)
                        throw new InvalidOperationException(UiText.Get("AppearanceColorDialogFailed"));
                    var font = SendMessage(GetDlgItem(window, 1), 0x31, 0, 0);
                    SendMessage(label, 0x30, (nuint)font, 1);
                    SendMessage(input, 0x30, (nuint)font, 1);
                    SendMessage(input, 0xc5, 3, 0);
                    state->InitializationFailed = false;
                    break;
                }
                case 0x111 when (wParam & 0xffff) == 1:
                {
                    var state = (AlphaState*)GetWindowLongPtr(window, -21);
                    if (state is not null)
                    {
                        Span<char> value = stackalloc char[4];
                        fixed (char* text = value)
                        {
                            var count = GetWindowText(GetDlgItem(window, AlphaControl), text, value.Length);
                            if (int.TryParse(value[..Math.Max(0, count)], NumberStyles.None,
                                    CultureInfo.InvariantCulture,
                                    out var percent) && percent != state->InitialPercent)
                                state->Alpha = (byte)Math.Round(Math.Clamp(percent, 0, 100) * 255d / 100);
                        }
                    }

                    break;
                }
            }

            return 0;
        }

        [LibraryImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
        private static partial int ChooseColor(ChooseColorData* data);

        [LibraryImport("comdlg32.dll")]
        private static partial uint CommDlgExtendedError();

        [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateWindowEx(uint style, string name, string text, uint windowStyle, int x, int y,
            int width, int height, nint owner, nint menu, nint instance, nint parameter);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        private static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
        private static partial int GetWindowText(nint window, char* text, int count);

        [LibraryImport("user32.dll")]
        private static partial nint GetDlgItem(nint window, int id);

        [LibraryImport("user32.dll")]
        private static partial uint GetDpiForWindow(nint window);

        [LibraryImport("user32.dll")]
        private static partial int GetClientRect(nint window, out NativeRect rect);

        [LibraryImport("user32.dll")]
        private static partial int GetWindowRect(nint window, out NativeRect rect);

        [LibraryImport("user32.dll")]
        private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height,
            uint flags);

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static partial nint GetWindowLongPtr(nint window, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static partial nint SetWindowLongPtr(nint window, int index, nint value);

        [StructLayout(LayoutKind.Sequential)]
        private struct AlphaState
        {
            public byte Alpha;
            public int InitialPercent;
            public char* Label;
            public bool InitializationFailed;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ChooseColorData
        {
            public uint Size;
            public nint Owner, Instance;
            public uint Color;
            public uint* CustomColors;
            public uint Flags;
            public nint Data;
            public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint> HookProcedure;
            public nint Template;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }
    }
}
