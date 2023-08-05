using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider)
        {
            switch (provider)
            {
                case 1:
                    return new DXGIScreenFrameProvider();
                default:
                    return new BitbltScreenFrameProvider();
            }
        }
    }
}
