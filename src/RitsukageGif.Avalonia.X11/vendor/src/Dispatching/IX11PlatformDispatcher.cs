using Avalonia.Threading;

namespace Avalonia.X11.Dispatching
{
    internal interface IX11PlatformDispatcher : IDispatcherImpl
    {
        X11EventDispatcher EventDispatcher { get; }
    }
}
