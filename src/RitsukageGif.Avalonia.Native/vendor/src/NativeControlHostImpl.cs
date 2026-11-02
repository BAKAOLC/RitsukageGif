using System;
using Avalonia.Controls.Platform;
using Avalonia.Native.Interop;
using Avalonia.Platform;
using MicroCom.Runtime;

namespace Avalonia.Native
{
    internal class NativeControlHostImpl : IDisposable, INativeControlHostImpl
    {
        private IAvnNativeControlHost? _host;

        // ReSharper disable once ConvertToPrimaryConstructor
        public NativeControlHostImpl(IAvnNativeControlHost host)
        {
            _host = host;
        }

        private IAvnNativeControlHost Host
        {
            get
            {
                ObjectDisposedException.ThrowIf(_host is null, this);
                return _host;
            }
        }

        public void Dispose()
        {
            _host?.Dispose();
            _host = null;
        }

        public INativeControlHostDestroyableControlHandle CreateDefaultChild(IPlatformHandle parent)
        {
            return new DestroyableNsView(Host);
        }

        public INativeControlHostControlTopLevelAttachment CreateNewAttachment(
            Func<IPlatformHandle, IPlatformHandle> create)
        {
            var a = new Attachment(Host.CreateAttachment());
            try
            {
                var child = create(a.GetParentHandle());
                a.InitWithChild(child);
                a.AttachedTo = this;
                return a;
            }
            catch
            {
                a.Dispose();
                throw;
            }
        }

        public INativeControlHostControlTopLevelAttachment CreateNewAttachment(IPlatformHandle handle)
        {
            var a = new Attachment(Host.CreateAttachment());
            a.InitWithChild(handle);
            a.AttachedTo = this;
            return a;
        }

        public bool IsCompatibleWith(IPlatformHandle handle)
        {
            return handle.HandleDescriptor == "NSView";
        }

        private class DestroyableNsView : INativeControlHostDestroyableControlHandle
        {
            private IAvnNativeControlHost? _impl;

            public DestroyableNsView(IAvnNativeControlHost impl)
            {
                _impl = impl.CloneReference();
                Handle = _impl.CreateDefaultChild(IntPtr.Zero);
            }

            public IntPtr Handle { get; private set; }

            public string HandleDescriptor => "NSView";

            public void Destroy()
            {
                if (_impl != null)
                {
                    _impl.DestroyDefaultChild(Handle);
                    _impl.Dispose();
                    _impl = null;
                    Handle = IntPtr.Zero;
                }
            }
        }

        private class Attachment : INativeControlHostControlTopLevelAttachment
        {
            private IAvnNativeControlHostTopLevelAttachment? _native;
            private NativeControlHostImpl? _attachedTo;

            // ReSharper disable once ConvertToPrimaryConstructor
            public Attachment(IAvnNativeControlHostTopLevelAttachment native)
            {
                _native = native;
            }

            private IAvnNativeControlHostTopLevelAttachment Native
            {
                get
                {
                    ObjectDisposedException.ThrowIf(_native is null, this);
                    return _native;
                }
            }

            public void Dispose()
            {
                if (_native != null)
                {
                    _native.ReleaseChild();
                    _native.Dispose();
                    _native = null;
                }
            }

            public INativeControlHostImpl? AttachedTo
            {
                get => _attachedTo;
                set
                {
                    var host = (NativeControlHostImpl?)value;
                    Native.AttachTo(host?._host);
                    _attachedTo = host;
                }
            }

            public bool IsCompatibleWith(INativeControlHostImpl host)
            {
                return host is NativeControlHostImpl;
            }

            public void HideWithSize(Size size)
            {
                Native.HideWithSize(Math.Max(1, (float)size.Width), Math.Max(1, (float)size.Height));
            }

            public void ShowInBounds(Rect bounds)
            {
                if (_attachedTo == null)
                    throw new InvalidOperationException("Native control isn't attached to a toplevel");
                bounds = new(bounds.X, bounds.Y, Math.Max(1, bounds.Width),
                    Math.Max(1, bounds.Height));
                Native.ShowInBounds((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
            }

            public PlatformHandle GetParentHandle()
            {
                return new(Native.ParentHandle, "NSView");
            }

            public void InitWithChild(IPlatformHandle handle)
            {
                Native.InitializeWithChildHandle(handle.Handle);
            }
        }
    }
}
