using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal sealed unsafe partial class MacTrayIcon : PortableTrayIcon
    {
        private readonly GCHandle _state;
        private nint _native;

        internal override bool SupportsDoubleClick => true;

        internal MacTrayIcon()
        {
            _state = GCHandle.Alloc(this);
            try
            {
                _native = NativeCreate((nint)(delegate* unmanaged[Cdecl]<nint, int, int, void>)&Callback,
                    GCHandle.ToIntPtr(_state));
                if (_native == 0) throw new IOException("The macOS status item could not be created.");
                IsReady = HostAvailable = true;
            }
            catch
            {
                _state.Free();
                throw;
            }
        }
        private const string Library = "ritsukagegif-tray";

        public override void SetIcon(IWindowIconImpl? icon)
        {
            if (icon is null)
            {
                NativeIcon(_native, null, 0);
                return;
            }

            using var stream = new MemoryStream();
            icon.Save(stream);
            var bytes = stream.GetBuffer();
            fixed (byte* address = bytes) NativeIcon(_native, address, checked((int)stream.Length));
        }

        public override void SetToolTipText(string? text)
        {
            NativeText(_native, text ?? "");
        }

        public override void SetIsVisible(bool visible)
        {
            NativeVisible(_native, visible ? 1 : 0);
        }

        protected override void PublishMenu()
        {
            if (IsDisposed) return;
            var entries = MenuModel.Capture();
            NativeMenuReset(_native);
            foreach (var entry in entries)
                NativeMenuItem(_native, entry.Id, entry.Parent, entry.Enabled ? 1 : 0, entry.Toggle,
                    entry.Checked ? 1 : 0, entry.Separator ? 1 : 0, entry.Label,
                    TrayGestureEncoding.MacKey(entry.Gesture), TrayGestureEncoding.MacModifiers(entry.Gesture));
            NativeMenuCommit(_native);
        }

        protected override void DisposeNative()
        {
            if (_native != 0) NativeDispose(_native);
            _native = 0;
            // ReSharper disable once PossiblyImpureMethodCallOnReadonlyVariable
            _state.Free();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Callback(nint state, int kind, int identifier)
        {
            var icon = (MacTrayIcon)GCHandle.FromIntPtr(state).Target!;
            if (icon.IsDisposed) return;
            try
            {
                switch (kind)
                {
                    case 1: icon.Click(TrayMouseGesture.SingleClick); break;
                    case 2: icon.Click(TrayMouseGesture.DoubleClick); break;
                    case 3: icon.Click(TrayMouseGesture.MiddleClick); break;
                    case 4: icon.MenuModel.RequestUpdate(identifier); break;
                    case 5: icon.MenuModel.Invoke(identifier); break;
                    case 6: icon.MenuModel.Open(identifier); break;
                    case 7: icon.MenuModel.Close(identifier); break;
                }
            }
            catch (Exception exception)
            {
                LogHub.GetLogger("Desktop.Tray")
                    .ZLogError(exception, $"macOS status item callback failed. Event={kind}");
            }
        }

        [LibraryImport(Library, EntryPoint = "rg_tray_create")]
        private static partial nint NativeCreate(nint callback, nint state);

        [LibraryImport(Library, EntryPoint = "rg_tray_dispose")]
        private static partial void NativeDispose(nint tray);

        [LibraryImport(Library, EntryPoint = "rg_tray_icon")]
        private static partial void NativeIcon(nint tray, byte* data, int length);

        [LibraryImport(Library, EntryPoint = "rg_tray_visible")]
        private static partial void NativeVisible(nint tray, int visible);

        [LibraryImport(Library, EntryPoint = "rg_tray_text", StringMarshalling = StringMarshalling.Utf8)]
        private static partial void NativeText(nint tray, string text);

        [LibraryImport(Library, EntryPoint = "rg_tray_menu_reset")]
        private static partial void NativeMenuReset(nint tray);

        [LibraryImport(Library, EntryPoint = "rg_tray_menu_item", StringMarshalling = StringMarshalling.Utf8)]
        private static partial void NativeMenuItem(nint tray, int identifier, int parent, int enabled, int toggle,
            int check, int separator, string label, string key, uint modifiers);

        [LibraryImport(Library, EntryPoint = "rg_tray_menu_commit")]
        private static partial void NativeMenuCommit(nint tray);
    }
}
