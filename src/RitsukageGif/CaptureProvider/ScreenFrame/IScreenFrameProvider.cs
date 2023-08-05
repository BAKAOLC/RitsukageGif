using System;
using System.Drawing;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public interface IScreenFrameProvider : IDisposable
    {
        void Initialize();

        void ApplyCaptureRegion(Rectangle rect);

        Bitmap Capture(bool cursor = false, double scale = 1);
    }
}