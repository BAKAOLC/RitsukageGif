using Avalonia.OpenGL.Egl;
using Avalonia.Platform.Surfaces;

namespace Avalonia.Win32
{
    internal interface IWindowsSurfaceFactory
    {
        bool RequiresNoRedirectionBitmap { get; }

        IPlatformRenderSurface CreateSurface(EglGlPlatformSurface.IEglWindowGlPlatformSurfaceInfo info);
    }
}
