using System;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Native.Interop;
using Avalonia.Platform;
using MicroCom.Runtime;

namespace Avalonia.Native
{
    internal class WindowImpl : WindowBaseImpl, IWindowImpl
    {
        private readonly AvaloniaNativePlatformOptions _opts;
        private double _extendTitleBarHeight = -1;
        private readonly DoubleClickHelper _doubleClickHelper;
        private readonly ITopLevelNativeMenuExporter _nativeMenuExporter;
        private bool _canResize = true;
        private bool _canMaximize = true;
        private WindowDecorations _decorations = WindowDecorations.Full;

        internal WindowImpl(IAvaloniaNativeFactory factory, AvaloniaNativePlatformOptions opts) : base(factory)
        {
            _opts = opts;
            _doubleClickHelper = new();

            using (var e = new WindowEvents(this)) Init(new(Native = factory.CreateWindow(e)));

            _nativeMenuExporter = new AvaloniaNativeMenuExporter(Native, factory);
        }

        public new IAvnWindow Native { get; }

        public WindowState WindowState
        {
            get => (WindowState)Native.WindowState;
            set => Native.SetWindowState((AvnWindowState)value);
        }

        public bool WindowStateGetterIsUsable => false;

        public void CanResize(bool value)
        {
            _canResize = value;
            Native.SetCanResize(value.AsComBool());
        }

        public void SetCanMinimize(bool value)
        {
            Native.SetCanMinimize(value.AsComBool());
        }

        public void SetCanMaximize(bool value)
        {
            _canMaximize = value;
            Native.SetCanMaximize(value.AsComBool());
        }

        public void SetTitle(string? title)
        {
            Native.SetTitle(title ?? "");
        }
        public Action<WindowState>? WindowStateChanged { get; set; }

        public Action<bool>? ExtendClientAreaToDecorationsChanged { get; set; }

        // Extension is handled by native backend
        public PlatformRequestedDrawnDecoration RequestedDrawnDecorations => default;
        public Thickness ExtendedMargins { get; private set; }

        public Thickness OffScreenMargin { get; } = new();

        public IntPtr? ZOrder => Native.WindowZOrder;
        public bool IsClientAreaExtendedToDecorations { get; private set; }

        public override void Show(bool activate, bool isDialog)
        {
            base.Show(activate, isDialog);

            InvalidateExtendedMargins();
        }

        /// <inheritdoc />
        public void SetExtendClientAreaToDecorationsHint(bool extendIntoClientAreaHint)
        {
            IsClientAreaExtendedToDecorations = extendIntoClientAreaHint;

            Native.SetExtendClientArea(extendIntoClientAreaHint.AsComBool());

            InvalidateExtendedMargins();
        }

        /// <inheritdoc />
        public void SetExtendClientAreaTitleBarHeightHint(double titleBarHeight)
        {
            _extendTitleBarHeight = titleBarHeight;
            Native.SetExtendTitleBarHeight(titleBarHeight);

            InvalidateExtendedMargins();
        }

        /// <inheritdoc />
        public bool NeedsManagedDecorations => false;

        public void ShowTaskbarIcon(bool value)
        {
            // NO OP On OSX
        }

        public void SetIcon(IWindowIconImpl? icon)
        {
            // NO OP on OSX
        }

        public Func<WindowCloseReason, bool>? Closing { get; set; }

        public void Move(PixelPoint point)
        {
            Position = point;
        }

        public override IPopupImpl? CreatePopup()
        {
            return _opts.OverlayPopups ? null : new PopupImpl(Factory, this);
        }

        public Action? GotInputWhenDisabled { get; set; }

        public void SetParent(IWindowImpl? parent)
        {
            Native.SetParent(((WindowImpl?)parent)?.Native);
        }

        public void SetEnabled(bool enable)
        {
            Native.SetEnabled(enable.AsComBool());

            // Showing a dialog should result in mouse capture being lost. macOS doesn't have the concept of mouse
            // capture, so no we have no OS-level event to hook into. Instead, release the mouse capture when the
            // owner window is disabled. This behavior matches win32, which sends a WM_CANCELMODE message when
            // EnableWindow(hWnd, false) is called from SetEnabled.
            if (!enable && MouseDevice is MouseDevice mouse)
                mouse.PlatformCaptureLost();
        }

        public override object? TryGetFeature(Type featureType)
        {
            if (featureType == typeof(ITextInputMethodImpl)) return InputMethod;

            if (featureType == typeof(ITopLevelNativeMenuExporter)) return _nativeMenuExporter;

            return base.TryGetFeature(featureType);
        }

        public void SetWindowDecorations(WindowDecorations enabled)
        {
            _decorations = enabled;
            Native.SetDecorations((SystemDecorations)enabled);
            InvalidateExtendedMargins();
        }

        internal sealed override void Init(MacOsTopLevelHandle handle)
        {
            base.Init(handle);
        }

        protected override bool ChromeHitTest(RawPointerEventArgs e)
        {
            if (IsClientAreaExtendedToDecorations)
                if (e.Type == RawPointerEventType.LeftButtonDown)
                {
                    // TODO: Casts like this are evil
                    var source = (PresentationSource?)InputRoot;
                    var visual = source?.Renderer.HitTestFirst(e.Position, source.RootElement, x =>
                    {
                        if (x is IInputElement ie && (!ie.IsHitTestVisible || !ie.IsEffectivelyVisible)) return false;
                        return true;
                    });

                    if (visual == null || WindowDecorationProperties.GetElementRole(visual) ==
                        WindowDecorationsElementRole.TitleBar)
                    {
                        if (_doubleClickHelper.IsDoubleClick(e.Timestamp, e.Position))
                            // ReSharper disable once ConvertSwitchStatementToSwitchExpression
                            switch (WindowState)
                            {
                                case WindowState.Maximized or WindowState.FullScreen
                                    when _canResize:
                                    WindowState = WindowState.Normal;
                                    break;

                                case WindowState.Normal
                                    when _canMaximize:
                                    WindowState = WindowState.Maximized;
                                    break;
                            }
                        else
                            Native.BeginMoveDrag();
                    }
                }

            return false;
        }

        private void InvalidateExtendedMargins()
        {
            if (Native is MicroComProxyBase { IsDisposed: true })
                return;

            if (WindowState == WindowState.FullScreen || !IsClientAreaExtendedToDecorations ||
                _decorations != WindowDecorations.Full)
                ExtendedMargins = new();
            else
                ExtendedMargins = new(0,
                    _extendTitleBarHeight.Equals(-1d) ? Native.ExtendTitleBarHeight : _extendTitleBarHeight, 0, 0);

            ExtendClientAreaToDecorationsChanged?.Invoke(IsClientAreaExtendedToDecorations);
        }

        private class WindowEvents : WindowBaseEvents, IAvnWindowEvents
        {
            private readonly WindowImpl _parent;

            // ReSharper disable once ConvertToPrimaryConstructor
            public WindowEvents(WindowImpl parent) : base(parent)
            {
                _parent = parent;
            }

            int IAvnWindowEvents.Closing()
            {
                if (_parent.Closing != null) return _parent.Closing(WindowCloseReason.WindowClosing).AsComBool();

                return true.AsComBool();
            }

            void IAvnWindowEvents.WindowStateChanged(AvnWindowState state)
            {
                _parent.InvalidateExtendedMargins();

                _parent.WindowStateChanged?.Invoke((WindowState)state);
            }

            void IAvnWindowEvents.GotInputWhenDisabled()
            {
                _parent.GotInputWhenDisabled?.Invoke();
            }
        }
    }
}
