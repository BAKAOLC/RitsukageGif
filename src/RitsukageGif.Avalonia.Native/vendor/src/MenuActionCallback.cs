using System;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class MenuActionCallback : NativeCallbackBase, IAvnActionCallback
    {
        private readonly Action _action;

        // ReSharper disable once ConvertToPrimaryConstructor
        public MenuActionCallback(Action action)
        {
            _action = action;
        }

        void IAvnActionCallback.Run()
        {
            _action.Invoke();
        }
    }
}
