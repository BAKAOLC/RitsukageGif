using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;
using Avalonia.Vulkan;

namespace Avalonia.X11.Vulkan
{
    internal class VulkanSupport
    {
        private static IntPtr s_offscreenWindow;

        [DllImport("libvulkan.so.1")]
        private static extern IntPtr vkGetInstanceProcAddr(IntPtr instance, string name);

        public static VulkanPlatformGraphics? TryInitialize(X11Info info, VulkanOptions? options)
        {
            s_offscreenWindow = XLib.XCreateSimpleWindow(info.DeferredDisplay,
                XLib.XDefaultRootWindow(info.DeferredDisplay), 0, 0, 1,
                1, 1, IntPtr.Zero, IntPtr.Zero);
            XLib.XSync(info.DeferredDisplay, true);

            return VulkanPlatformGraphics.TryCreate(options ?? new(), new()
            {
                RequiredInstanceExtensions = { "VK_KHR_xlib_surface" },
                GetProcAddressDelegate = vkGetInstanceProcAddr,
                DeviceCheckSurfaceFactory = instance => CreateDummySurface(info.DeferredDisplay, instance),
                PlatformFeatures = new()
                {
                    [typeof(IVulkanKhrSurfacePlatformSurfaceFactory)] = new VulkanSurfaceFactory(info.DeferredDisplay),
                },
            });
        }

        private static ulong CreateDummySurface(IntPtr display, IVulkanInstance instance)
        {
            var surf = CreateXlibSurface(display, s_offscreenWindow, instance);
            XLib.XSync(display, true);
            return surf;
        }

        private static ulong CreateXlibSurface(IntPtr display, IntPtr window, IVulkanInstance instance)
        {
            var vulkanXlib = new X11VulkanInterface(instance);
            var createInfo = new VkXlibSurfaceCreateInfoKHR
            {
                sType = VkXlibSurfaceCreateInfoKHR.VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR,
                dpy = display,
                window = window,
            };
            VulkanException.ThrowOnError("vkCreateXlibSurfaceKHR",
                vulkanXlib.vkCreateXlibSurfaceKHR(instance.Handle, ref createInfo, IntPtr.Zero, out var surface));
            return surface;
        }

        internal class VulkanSurfaceFactory : IVulkanKhrSurfacePlatformSurfaceFactory
        {
            private readonly IntPtr _display;

            public VulkanSurfaceFactory(IntPtr display)
            {
                _display = display;
            }

            public bool CanRenderToSurface(IVulkanPlatformGraphicsContext context, IPlatformRenderSurface surface)
            {
                return surface is INativePlatformHandleSurface handle && handle.HandleDescriptor == "XID";
            }

            public IVulkanKhrSurfacePlatformSurface CreateSurface(IVulkanPlatformGraphicsContext context,
                IPlatformRenderSurface handle)
            {
                return new XidSurface(_display, (INativePlatformHandleSurface)handle);
            }
        }

        private class XidSurface : IVulkanKhrSurfacePlatformSurface
        {
            private readonly IntPtr _display;
            private readonly INativePlatformHandleSurface _handle;

            public XidSurface(IntPtr display, INativePlatformHandleSurface handle)
            {
                _display = display;
                _handle = handle;
            }

            public void Dispose()
            {
                // No-op
            }

            public double Scaling => _handle.Scaling;
            public PixelSize Size => _handle.Size;

            public ulong CreateSurface(IVulkanPlatformGraphicsContext context)
            {
                return CreateXlibSurface(_display, _handle.Handle, context.Instance);
            }
        }
    }
}
