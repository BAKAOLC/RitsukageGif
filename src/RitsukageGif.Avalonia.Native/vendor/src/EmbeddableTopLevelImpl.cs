using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal sealed class EmbeddableTopLevelImpl : TopLevelImpl
    {
        public EmbeddableTopLevelImpl(IAvaloniaNativeFactory factory) : base(factory)
        {
            using var e = new TopLevelEvents(this);
            Init(new(factory.CreateTopLevel(e)));
        }
    }
}
