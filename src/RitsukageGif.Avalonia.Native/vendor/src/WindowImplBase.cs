using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Native.Interop;
using Avalonia.Platform;

namespace Avalonia.Native
{
    internal abstract class WindowBaseImpl : TopLevelImpl, IWindowBaseImpl
    {
        internal WindowBaseImpl(IAvaloniaNativeFactory factory) : base(factory)
        {
        }

        public new IAvnWindowBase? Native => NativeHandle?.Native as IAvnWindowBase;

        public PixelPoint Position
        {
            get => Native?.Position.ToAvaloniaPixelPoint() ?? default;
            protected set => Native?.SetPosition(value.ToAvnPoint());
        }

        public Action? Deactivated { get; set; }
        public Action? Activated { get; set; }

        public Action<PixelPoint>? PositionChanged { get; set; }

        public Size? FrameSize
        {
            get
            {
                if (Native != null)
                    unsafe
                    {
                        var s = new AvnSize { Width = -1, Height = -1 };
                        Native.GetFrameSize(&s);
                        return s is { Width: < 0, Height: < 0 } ? null : new Size(s.Width, s.Height);
                    }

                return null;
            }
        }

        public void Activate()
        {
            Native?.Activate();
        }

        public override void SetFrameThemeVariant(PlatformThemeVariant? themeVariant)
        {
            var settings = AvaloniaLocator.Current.GetService<IPlatformSettings>();
            themeVariant ??= settings?.GetColorValues().ThemeVariant ?? PlatformThemeVariant.Light;
            Native?.SetFrameThemeVariant((AvnPlatformThemeVariant)themeVariant);
        }

        public override void Dispose()
        {
            Native?.Close();
            base.Dispose();
        }

        public virtual void Show(bool activate, bool isDialog)
        {
            Native?.Show(activate.AsComBool(), isDialog.AsComBool());
        }

        public void Hide()
        {
            Native?.Hide();
        }

        public Size MaxAutoSizeHint => this.TryGetFeature<IScreenImpl>()!.AllScreens
            .Select(s => s.Bounds.Size.ToSize(1))
            .OrderByDescending(x => x.Width + x.Height).FirstOrDefault();

        public void SetTopmost(bool value)
        {
            Native?.SetTopMost(value.AsComBool());
        }

        internal override void Init(MacOsTopLevelHandle handle)
        {
            NativeHandle = handle;

            base.Init(handle);

            int defaultWidth = 0, defaultHeight = 0;

            var monitor = this.TryGetFeature<IScreenImpl>()!.AllScreens
                .OrderBy(x => x.Scaling)
                .FirstOrDefault(m => m.Bounds.Contains(Position));

            if (monitor != null)
            {
                // Emulate Windows 7+ default window size behavior.
                defaultWidth = (int)(monitor.WorkingArea.Width * 0.75d);
                defaultHeight = (int)(monitor.WorkingArea.Height * 0.7d);
            }

            defaultWidth = Math.Max(defaultWidth, 300);
            defaultHeight = Math.Max(defaultHeight, 200);

            Resize(new(defaultWidth, defaultHeight), WindowResizeReason.Layout);
        }

        public void Resize(Size clientSize, WindowResizeReason reason)
        {
            Native?.Resize(clientSize.Width, clientSize.Height, (AvnPlatformResizeReason)reason);
        }

        public void BeginMoveDrag(PointerPressedEventArgs e)
        {
            Native?.BeginMoveDrag();
        }

        // TODO
        [SuppressMessage("Performance", "CA1822")]
        public void BeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
        {
        }

        public void SetMinMaxSize(Size minSize, Size maxSize)
        {
            Native?.SetMinMaxSize(minSize.ToAvnSize(), maxSize.ToAvnSize());
        }

        protected class WindowBaseEvents : TopLevelEvents, IAvnWindowBaseEvents
        {
            private readonly WindowBaseImpl _parent;

            // ReSharper disable once ConvertToPrimaryConstructor
            protected WindowBaseEvents(WindowBaseImpl parent) : base(parent)
            {
                _parent = parent;
            }

            void IAvnWindowBaseEvents.PositionChanged(AvnPoint position)
            {
                _parent.PositionChanged?.Invoke(position.ToAvaloniaPixelPoint());
            }

            void IAvnWindowBaseEvents.Activated()
            {
                _parent.Activated?.Invoke();
            }

            void IAvnWindowBaseEvents.Deactivated()
            {
                _parent.Deactivated?.Invoke();
            }
        }
    }
}
