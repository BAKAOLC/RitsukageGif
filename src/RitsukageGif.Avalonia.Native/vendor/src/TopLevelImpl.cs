using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Input.TextInput;
using Avalonia.Native.Interop;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;
using Avalonia.Platform.Surfaces;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Avalonia.Native
{
    internal class MacOsTopLevelHandle : IPlatformHandle, IMacOSTopLevelPlatformHandle
    {
        internal MacOsTopLevelHandle(IAvnTopLevel native)
        {
            Native = native;

            HandleDescriptor = "NSView";

            Handle = NSView;
        }

        internal MacOsTopLevelHandle(IAvnWindowBase native)
        {
            Native = native;

            HandleDescriptor = "NSWindow";

            Handle = NSWindow;
        }

        internal IAvnTopLevel Native { get; }

        public IntPtr GetNSViewRetained()
        {
            return Native.ObtainNSViewHandleRetained();
        }

        public IntPtr GetNSWindowRetained()
        {
            return (Native as IAvnWindowBase)?.ObtainNSWindowHandleRetained() ?? IntPtr.Zero;
        }

        public IntPtr Handle { get; }

        public string HandleDescriptor { get; }

        public IntPtr NSView => Native.ObtainNSViewHandle();

        public IntPtr NSWindow => (Native as IAvnWindowBase)?.ObtainNSWindowHandle() ?? IntPtr.Zero;
    }

    internal class TopLevelImpl : ITopLevelImpl, IFramebufferPlatformSurface
    {
        protected IInputRoot? InputRoot;
        private NativeControlHostImpl? _nativeControlHost;
        private PlatformBehaviorInhibition? _platformBehaviorInhibition;

        private readonly MouseDevice _mouse;
        private readonly PenDevice _pen;

        private readonly IKeyboardDevice? _keyboard;
        private readonly ICursorFactory? _cursorFactory;
        protected readonly IAvaloniaNativeFactory Factory;

        private Size _savedLogicalSize;

        // ReSharper disable once ReplaceWithFieldKeyword
        private WindowTransparencyLevel _transparencyLevel = WindowTransparencyLevel.None;

        protected MacOsTopLevelHandle? NativeHandle;

        private readonly object _syncRoot = new();
        private IPlatformRenderSurface[]? _surfaces;

        // ReSharper disable once ConvertToPrimaryConstructor
        protected TopLevelImpl(IAvaloniaNativeFactory factory)
        {
            Factory = factory;

            _keyboard = AvaloniaLocator.Current.GetService<IKeyboardDevice>();
            _mouse = Avalonia.Input.MouseDevice.Primary;
            _pen = new();
            _cursorFactory = AvaloniaLocator.Current.GetService<ICursorFactory>();
        }

        public double DesktopScaling => 1;

        public IAvnTopLevel? Native => NativeHandle?.Native;
        public IPlatformHandle? Handle => NativeHandle;

        IFramebufferRenderTarget IFramebufferPlatformSurface.CreateFramebufferRenderTarget()
        {
            if (!Dispatcher.UIThread.CheckAccess())
                throw new RenderTargetNotReadyException();

            var nativeRenderTarget = Native?.CreateSoftwareRenderTarget();

            if (nativeRenderTarget is null) throw new RenderTargetNotReadyException();

            return new FramebufferRenderTarget(this, nativeRenderTarget);
        }
        public AvaloniaNativeTextInputMethod? InputMethod { get; protected set; }

        public Size ClientSize
        {
            get
            {
                if (Native == null) return default;

                var s = Native.ClientSize;
                return new(s.Width, s.Height);
            }
        }

        public double RenderScaling { get; private set; }

        public IPlatformRenderSurface[] Surfaces => _surfaces ?? [];
        public Action<RawInputEventArgs>? Input { get; set; }
        public Action<Rect>? Paint { get; set; }
        public Action<Size, WindowResizeReason>? Resized { get; set; }
        public Action<double>? ScalingChanged { get; set; }
        public Action<WindowTransparencyLevel>? TransparencyLevelChanged { get; set; }
        public Compositor Compositor => AvaloniaNativePlatform.Compositor;
        public Action? Closed { get; set; }
        public Action? LostFocus { get; set; }

        public WindowTransparencyLevel TransparencyLevel
        {
            get => _transparencyLevel;
            private set
            {
                if (_transparencyLevel != value)
                {
                    _transparencyLevel = value;
                    TransparencyLevelChanged?.Invoke(value);
                }
            }
        }

        public AcrylicPlatformCompensationLevels AcrylicCompensationLevels { get; } = new(1, 0, 0);

        protected IMouseDevice MouseDevice => _mouse;

        public virtual void SetFrameThemeVariant(PlatformThemeVariant? themeVariant)
        {
            //noop
        }

        public void SetInputRoot(IInputRoot inputRoot)
        {
            InputRoot = inputRoot;
        }

        public Point PointToClient(PixelPoint point)
        {
            return Native?.PointToClient(point.ToAvnPoint()).ToAvaloniaPoint() ?? default;
        }

        public PixelPoint PointToScreen(Point point)
        {
            return Native?.PointToScreen(point.ToAvnPoint()).ToAvaloniaPixelPoint() ?? default;
        }

        public void SetCursor(ICursorImpl? cursor)
        {
            if (Native == null) return;

            var newCursor = cursor as AvaloniaNativeCursor;
            newCursor ??= _cursorFactory?.GetCursor(StandardCursorType.Arrow) as AvaloniaNativeCursor;
            Native.SetCursor(newCursor?.Cursor);
        }

        public virtual IPopupImpl? CreatePopup()
        {
            return new PopupImpl(Factory, this);
        }

        public void SetTransparencyLevelHint(IReadOnlyList<WindowTransparencyLevel> transparencyLevels)
        {
            foreach (var level in transparencyLevels)
            {
                AvnWindowTransparencyMode? mode = null;

                if (level == WindowTransparencyLevel.None)
                    mode = AvnWindowTransparencyMode.Opaque;
                if (level == WindowTransparencyLevel.Transparent)
                    mode = AvnWindowTransparencyMode.Transparent;
                else if (level == WindowTransparencyLevel.AcrylicBlur)
                    mode = AvnWindowTransparencyMode.Blur;

                if (mode.HasValue && level != TransparencyLevel)
                {
                    Native?.SetTransparencyMode(mode.Value);
                    TransparencyLevel = level;
                    return;
                }
            }

            // If we get here, we didn't find a supported level. Use the default of None.
            if (TransparencyLevel != WindowTransparencyLevel.None)
            {
                Native?.SetTransparencyMode(AvnWindowTransparencyMode.Opaque);
                TransparencyLevel = WindowTransparencyLevel.None;
            }
        }

        public virtual object? TryGetFeature(Type featureType)
        {
            if (featureType == typeof(ITextInputMethodImpl)) return InputMethod;

            if (featureType == typeof(INativeControlHostImpl)) return _nativeControlHost;

            if (featureType == typeof(IPlatformBehaviorInhibition)) return _platformBehaviorInhibition;

            if (featureType == typeof(IClipboard)) return AvaloniaLocator.Current.GetRequiredService<IClipboard>();

            if (featureType == typeof(IScreenImpl)) return AvaloniaLocator.Current.GetRequiredService<IScreenImpl>();

            if (featureType == typeof(ILauncher)) return new BclLauncher();

            return null;
        }

        public virtual void Dispose()
        {
            Native?.Dispose();
            NativeHandle = null;

            _nativeControlHost?.Dispose();
            _nativeControlHost = null;
        }

        internal virtual void Init(MacOsTopLevelHandle handle)
        {
            NativeHandle = handle;
            _savedLogicalSize = ClientSize;
            RenderScaling = Native?.Scaling ?? 1;
            _nativeControlHost = new(Native!.CreateNativeControlHost());
            _platformBehaviorInhibition = new(Factory.CreatePlatformBehaviorInhibition());
            _surfaces = [new GlPlatformSurface(Native), new MetalPlatformSurface(Native), this];
            InputMethod = new(Native);
        }

        internal void BeginDraggingSession(
            AvnDragDropEffects effects,
            AvnPoint point,
            IAvnClipboardDataSource source,
            IAvnDndResultCallback callback,
            IntPtr sourceHandle)
        {
            Native?.BeginDragAndDropOperation(effects, point, source, callback, sourceHandle);
        }

        private AutomationPeer? GetAutomationPeer()
        {
            return InputRoot?.FocusRoot is Control c ? ControlAutomationPeer.CreatePeerForElement(c) : null;
        }

        private bool RawTextInputEvent(ulong timeStamp, string text)
        {
            if (InputRoot is null)
                return false;

            if (_keyboard is null) return false;

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input + 1);

            var args = new RawTextInputEventArgs(_keyboard, timeStamp, InputRoot, text);

            Input?.Invoke(args);

            return args.Handled;
        }

        private bool RawKeyEvent(
            AvnRawKeyEventType type,
            ulong timeStamp,
            AvnInputModifiers modifiers,
            AvnKey key,
            AvnPhysicalKey physicalKey,
            string keySymbol)
        {
            if (InputRoot is null)
                return false;

            if (_keyboard is null) return false;

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input + 1);

            var args = new RawKeyEventArgs(
                _keyboard,
                timeStamp,
                InputRoot,
                (RawKeyEventType)type,
                (Key)key,
                (RawInputModifiers)modifiers,
                (PhysicalKey)physicalKey,
                keySymbol);

            Input?.Invoke(args);

            return args.Handled;
        }

        private void RawMouseEvent(AvnRawMouseEventType type, AvnPointerDeviceType deviceType, ulong timeStamp,
            AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure, float xTilt, float yTilt)
        {
            if (InputRoot is null)
                return;

            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input + 1);

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (type)
            {
                case AvnRawMouseEventType.Wheel:
                    Input?.Invoke(new RawMouseWheelEventArgs(_mouse, timeStamp, InputRoot,
                        point.ToAvaloniaPoint(), new(delta.X, delta.Y), (RawInputModifiers)modifiers));
                    break;

                case AvnRawMouseEventType.Magnify:
                    Input?.Invoke(new RawPointerGestureEventArgs(_mouse, timeStamp, InputRoot,
                        RawPointerEventType.Magnify,
                        point.ToAvaloniaPoint(), new(delta.X, delta.Y), (RawInputModifiers)modifiers));
                    break;

                case AvnRawMouseEventType.Rotate:
                    Input?.Invoke(new RawPointerGestureEventArgs(_mouse, timeStamp, InputRoot,
                        RawPointerEventType.Rotate,
                        point.ToAvaloniaPoint(), new(delta.X, delta.Y), (RawInputModifiers)modifiers));
                    break;

                case AvnRawMouseEventType.Swipe:
                    Input?.Invoke(new RawPointerGestureEventArgs(_mouse, timeStamp, InputRoot,
                        RawPointerEventType.Swipe,
                        point.ToAvaloniaPoint(), new(delta.X, delta.Y), (RawInputModifiers)modifiers));
                    break;

                default:
                    IInputDevice device = deviceType == AvnPointerDeviceType.Pen ? _pen : _mouse;
                    var e = new RawPointerEventArgs(device, timeStamp, InputRoot, (RawPointerEventType)type,
                        new RawPointerPoint
                        {
                            Position = point.ToAvaloniaPoint(),
                            Pressure = pressure,
                            XTilt = xTilt,
                            YTilt = yTilt,
                        }, (RawInputModifiers)modifiers);

                    if (!ChromeHitTest(e)) Input?.Invoke(e);
                    break;
            }
        }

        protected virtual bool ChromeHitTest(RawPointerEventArgs e)
        {
            return false;
        }

        protected unsafe class TopLevelEvents : NativeCallbackBase, IAvnTopLevelEvents
        {
            private readonly TopLevelImpl _parent;

            // ReSharper disable once ConvertToPrimaryConstructor
            public TopLevelEvents(TopLevelImpl parent)
            {
                _parent = parent;
            }

            void IAvnTopLevelEvents.Closed()
            {
                var n = _parent.Native;

                try
                {
                    _parent.Closed?.Invoke();
                }
                finally
                {
                    _parent.Dispose();
                    n?.Dispose();
                }
            }

            void IAvnTopLevelEvents.Paint()
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.UiThreadRender);
                var s = _parent.ClientSize;
                _parent.Paint?.Invoke(new(0, 0, s.Width, s.Height));
            }

            void IAvnTopLevelEvents.Resized(AvnSize* size, AvnPlatformResizeReason reason)
            {
                if (_parent.Native == null) return;

                var s = new Size(size->Width, size->Height);
                _parent._savedLogicalSize = s;
                _parent.Resized?.Invoke(s, (WindowResizeReason)reason);
            }

            void IAvnTopLevelEvents.RawMouseEvent(AvnRawMouseEventType type, AvnPointerDeviceType pointerDeviceType,
                ulong timeStamp, AvnInputModifiers modifiers, AvnPoint point, AvnVector delta, float pressure,
                float xTilt, float yTilt)
            {
                _parent.RawMouseEvent(type, pointerDeviceType, timeStamp, modifiers, point, delta, pressure, xTilt,
                    yTilt);
            }

            int IAvnTopLevelEvents.RawKeyEvent(AvnRawKeyEventType type, ulong timeStamp, AvnInputModifiers modifiers,
                AvnKey key, AvnPhysicalKey physicalKey, string keySymbol)
            {
                return _parent.RawKeyEvent(type, timeStamp, modifiers, key, physicalKey, keySymbol).AsComBool();
            }

            int IAvnTopLevelEvents.RawTextInputEvent(ulong timeStamp, string text)
            {
                return _parent.RawTextInputEvent(timeStamp, text).AsComBool();
            }

            void IAvnTopLevelEvents.ScalingChanged(double scaling)
            {
                _parent.RenderScaling = scaling;
                _parent.ScalingChanged?.Invoke(scaling);
            }

            void IAvnTopLevelEvents.RunRenderPriorityJobs()
            {
                Dispatcher.UIThread.RunJobs(DispatcherPriority.UiThreadRender);
            }

            void IAvnTopLevelEvents.LostFocus()
            {
                _parent.LostFocus?.Invoke();

                // macOS doesn't have the concept of mouse capture. If we're losing the focus during an implicit capture
                // (standard mouse down), we should release it to avoid mouse events going to an old window.
                var mouse = _parent._mouse;
                var captured = mouse.Pointer.Captured;

                if (captured is not null &&
                    mouse.Pointer.CaptureSource == CaptureSource.Implicit &&
                    TopLevel.GetTopLevel(captured as Visual)?.PlatformImpl == _parent)
                    mouse.PlatformCaptureLost();
            }

            AvnDragDropEffects IAvnTopLevelEvents.DragEvent(AvnDragEventType type, AvnPoint position,
                AvnInputModifiers modifiers,
                AvnDragDropEffects effects,
                IAvnClipboard clipboard, IntPtr dataTransferHandle)
            {
                var device = AvaloniaLocator.Current.GetService<IDragDropDevice>();

                if (device is null || _parent.InputRoot is null) return AvnDragDropEffects.None;

                IDataTransfer? dataTransfer = null;
                if (dataTransferHandle != IntPtr.Zero)
                    dataTransfer = GCHandle.FromIntPtr(dataTransferHandle).Target as IDataTransfer;

                using var clipboardDataTransfer = new ClipboardDataTransfer(
                    new(clipboard, clipboard.ChangeCount, true));
                dataTransfer ??= clipboardDataTransfer;

                var args = new RawDragEvent(
                    device,
                    (RawDragEventType)type,
                    _parent.InputRoot,
                    position.ToAvaloniaPoint(),
                    dataTransfer,
                    (DragDropEffects)effects,
                    (RawInputModifiers)modifiers);
                _parent.Input?.Invoke(args);
                return (AvnDragDropEffects)args.Effects;
            }

            IAvnAutomationPeer? IAvnTopLevelEvents.AutomationPeer
            {
                get
                {
                    var native = _parent.GetAutomationPeer();

                    return native is null ? null : AvnAutomationPeer.Wrap(native);
                }
            }
        }

        private class FramebufferRenderTarget : IFramebufferRenderTarget
        {
            private readonly TopLevelImpl _parent;
            private IAvnSoftwareRenderTarget? _target;

            // ReSharper disable once ConvertToPrimaryConstructor
            public FramebufferRenderTarget(TopLevelImpl parent, IAvnSoftwareRenderTarget target)
            {
                _parent = parent;
                _target = target;
            }

            public void Dispose()
            {
                lock (_parent._syncRoot)
                {
                    _target?.Dispose();
                    _target = null;
                }
            }


            public ILockedFramebuffer Lock(IRenderTarget.RenderTargetSceneInfo sceneInfo,
                out FramebufferLockProperties properties)
            {
                ObjectDisposedException.ThrowIf(_target is null, this);
                properties = default;
                // ReSharper disable InconsistentlySynchronizedField
                var w = Math.Max(_parent._savedLogicalSize.Width * _parent.RenderScaling, 1);
                var h = Math.Max(_parent._savedLogicalSize.Height * _parent.RenderScaling, 1);
                var dpi = _parent.RenderScaling * 96;
                // ReSharper restore InconsistentlySynchronizedField
                return new DeferredFramebuffer(_target, cb =>
                {
                    lock (_parent._syncRoot)
                        if (_parent.Native != null && _target != null)
                            cb(_parent.Native);
                }, (int)w, (int)h, new(dpi, dpi));
            }

            public bool RetainsFrameContents => false;
        }
    }
}
