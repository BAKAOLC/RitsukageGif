using System;
using MicroCom.Runtime;

namespace Avalonia.Win32.DComposition
{
    internal class DirectCompositionShared : IDisposable
    {
        public DirectCompositionShared(IDCompositionDesktopDevice device)
        {
            Device = device.CloneReference();
        }

        public object SyncRoot { get; } = new();

        public IDCompositionDesktopDevice Device { get; }

        public void Dispose()
        {
            Device.Dispose();
        }
    }
}
