using System;
using Avalonia.Input;

namespace Avalonia.X11.Selections.DragDrop
{
    internal sealed class DragDropDataProvider : SelectionDataProvider
    {
        public DragDropDataProvider(AvaloniaX11Platform platform, IAsyncDataTransfer dataTransfer)
            : base(platform, platform.Info.Atoms.XdndSelection)
        {
            DataTransfer = dataTransfer;
        }

        public Action? Activity { get; set; }

        public new IntPtr GetOwner()
        {
            return base.GetOwner();
        }

        public new void SetOwner(IntPtr window)
        {
            base.SetOwner(window);
        }

        protected override void OnActivity()
        {
            Activity?.Invoke();
        }

        public override void Dispose()
        {
            Activity = null;

            DataTransfer?.Dispose();
            DataTransfer = null;
        }
    }
}
