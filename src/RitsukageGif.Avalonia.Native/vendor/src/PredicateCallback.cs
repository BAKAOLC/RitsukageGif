using System;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class PredicateCallback : NativeCallbackBase, IAvnPredicateCallback
    {
        private readonly Func<bool> _predicate;

        // ReSharper disable once ConvertToPrimaryConstructor
        public PredicateCallback(Func<bool> predicate)
        {
            _predicate = predicate;
        }

        int IAvnPredicateCallback.Evaluate()
        {
            return _predicate().AsComBool();
        }
    }
}
