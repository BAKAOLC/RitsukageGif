using System;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using MicroCom.Runtime;

namespace Avalonia.Native
{
    /// <summary>
    ///     Represents a COM object whose lifetime is completely handled by the native side.
    /// </summary>
    internal abstract class NativeOwned : IUnknown, IMicroComShadowContainer, IMicroComExceptionCallback
    {
        private MicroComShadow? _shadow;

        void IMicroComExceptionCallback.RaiseException(Exception e)
        {
            if (AvaloniaLocator.Current.GetService<IDispatcherImpl>() is DispatcherImpl dispatcherImpl)
                dispatcherImpl.PropagateCallbackException(ExceptionDispatchInfo.Capture(e));
        }

        MicroComShadow? IMicroComShadowContainer.Shadow
        {
            get => _shadow;
            set => _shadow = value;
        }

        void IMicroComShadowContainer.OnReferencedFromNative()
        {
        }

        void IMicroComShadowContainer.OnUnreferencedFromNative()
        {
            _shadow?.Dispose();
            _shadow = null;
            Destroyed();
        }

        void IDisposable.Dispose()
        {
        }

        protected virtual void Destroyed()
        {
        }
    }
}
