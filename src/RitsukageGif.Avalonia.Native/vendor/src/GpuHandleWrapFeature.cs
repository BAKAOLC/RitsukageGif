using System;
using Avalonia.Native.Interop;
using Avalonia.Platform;

namespace Avalonia.Native
{
    internal class GpuHandleWrapFeature : IExternalObjectsHandleWrapRenderInterfaceContextFeature
    {
        private readonly IAvnNativeObjectsMemoryManagement _helper;

        // ReSharper disable once ConvertToPrimaryConstructor
        public GpuHandleWrapFeature(IAvaloniaNativeFactory factory)
        {
            _helper = factory.CreateMemoryManagementHelper();
        }

        public IExternalObjectsWrappedGpuHandle? WrapImageHandleOnAnyThread(IPlatformHandle handle,
            PlatformGraphicsExternalImageProperties properties)
        {
            if (handle.HandleDescriptor == KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef)
            {
                _helper.RetainCFObject(handle.Handle);
                return new CfObjectWrapper(_helper, handle.Handle, handle.HandleDescriptor);
            }

            return null;
        }

        public IExternalObjectsWrappedGpuHandle? WrapSemaphoreHandleOnAnyThread(IPlatformHandle handle)
        {
            if (handle.HandleDescriptor == KnownPlatformGraphicsExternalSemaphoreHandleTypes.MetalSharedEvent)
            {
                _helper.RetainNSObject(handle.Handle);
                return new NsObjectWrapper(_helper, handle.Handle, handle.HandleDescriptor);
            }

            return null;
        }

        private class NsObjectWrapper(IAvnNativeObjectsMemoryManagement helper, IntPtr handle, string descriptor)
            : IExternalObjectsWrappedGpuHandle
        {
            public void Dispose()
            {
                helper.ReleaseNSObject(handle);
            }

            public IntPtr Handle => handle;
            public string HandleDescriptor => descriptor;
        }

        private class CfObjectWrapper(IAvnNativeObjectsMemoryManagement helper, IntPtr handle, string descriptor)
            : IExternalObjectsWrappedGpuHandle
        {
            public void Dispose()
            {
                helper.ReleaseCFObject(handle);
            }

            public IntPtr Handle => handle;
            public string HandleDescriptor => descriptor;
        }
    }
}
