using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;

namespace RitsukageGif.Desktop
{
    internal abstract class PortableTrayIcon : ITrayIconImpl, INativeMenuExporter
    {
        protected PortableTrayIcon()
        {
            MenuModel = new(PublishMenu);
        }

        protected PortableTrayMenu MenuModel { get; }
        protected bool IsDisposed { get; private set; }
        internal bool IsReady { get; private protected set; }
        internal bool HostAvailable { get; private protected set; }
        internal abstract bool SupportsDoubleClick { get; }
        internal virtual Task Completion => Task.CompletedTask;

        public void SetNativeMenu(NativeMenu? menu)
        {
            TrayContextPopup.CloseFor(MenuModel);
            MenuModel.SetMenu(menu);
        }

        public INativeMenuExporter MenuExporter => this;
        public Action? OnClicked { get; set; }

        public abstract void SetIcon(IWindowIconImpl? icon);
        public abstract void SetToolTipText(string? text);
        public abstract void SetIsVisible(bool visible);

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            MenuModel.Dispose();
            DisposeNative();
            IsReady = HostAvailable = false;
            PortableTrayState.Detach(this);
        }

        protected abstract void PublishMenu();
        protected abstract void DisposeNative();

        protected void Click(TrayMouseGesture gesture)
        {
            if (IsDisposed) return;
            PortableTrayState.Click(gesture);
        }
    }
}
