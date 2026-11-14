using System;
using System.Runtime.InteropServices;
using Avalonia.OpenGL.Egl;
using Avalonia.SourceGenerator;

namespace Avalonia.OpenGL.Angle
{
    internal partial class Win32AngleEglInterface : EglInterface
    {
        public Win32AngleEglInterface() : this(LoadAngle())
        {
        }

        private Win32AngleEglInterface(Func<string, IntPtr> getProcAddress) : base(getProcAddress)
        {
            Initialize(getProcAddress);
        }

        [DllImport("av_libGLESv2.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr EGL_GetProcAddress(string proc);

        [GetProcAddress("eglCreateDeviceANGLE", true)]
        public partial IntPtr CreateDeviceANGLE(int deviceType, IntPtr nativeDevice, int[]? attribs);

        [GetProcAddress("eglReleaseDeviceANGLE", true)]
        public partial void ReleaseDeviceANGLE(IntPtr device);

        private static Func<string, IntPtr> LoadAngle()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var disp = EGL_GetProcAddress("eglGetPlatformDisplayEXT");

                if (disp == IntPtr.Zero)
                    throw new OpenGlException("libegl.dll doesn't have eglGetPlatformDisplayEXT entry point");

                return EGL_GetProcAddress;
            }

            throw new PlatformNotSupportedException();
        }
    }
}
