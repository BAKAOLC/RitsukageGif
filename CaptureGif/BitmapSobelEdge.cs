using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CaptureGif
{
    public class BitmapSobelEdge
    {
        public int Width { get; }

        public int Height { get; }

        public Bitmap Bitmap { get; }

        public int Stride { get; }
        
        public Rectangle Rect { get; }

        public byte[] Data { get; }

        private BitmapSobelEdge(Bitmap bitmap)
        {
            Bitmap = bitmap;
            Width = Bitmap.Width;
            Height = Bitmap.Height;
            var  data = Bitmap.LockBits(Rect, ImageLockMode.ReadWrite, Bitmap.PixelFormat);
            Stride = data.Stride;
            Data = new byte[Stride * Height];
            Bitmap.UnlockBits(data);
        }

        public void ProcessToData()
        {
            var data = Bitmap.LockBits(Rect, ImageLockMode.ReadWrite, Bitmap.PixelFormat);
            Marshal.Copy(data.Scan0, Data, 0, Data.Length);
            Bitmap.UnlockBits(data);
        }

        public void ProcessToBitmap()
        {
            var data = Bitmap.LockBits(Rect, ImageLockMode.ReadWrite, Bitmap.PixelFormat);
            Marshal.Copy(Data, 0, data.Scan0, data.Stride * Height);
            Bitmap.UnlockBits(data);
        }

        public byte GetPixelB(int x, int y)
        {
            return Data[Stride * y + Stride / Width * x];
        }

        public byte GetPixelG(int x, int y)
        {
            return Data[Stride * y + Stride / Width * x + 1];
        }

        public byte GetPixelR(int x, int y)
        {
            return Data[Stride * y + Stride / Width * x + 2];
        }

        public byte GetPixelGray(int x, int y)
        {
            var num = Stride * y + Stride / Width * x;
            return (byte)((Data[num] + Data[num + 1] + Data[num + 2]) / 3);
        }

        public void SetPixelB(int x, int y, byte color)
        {
            Data[Stride * y + Stride / Width * x] = color;
        }

        public void SetPixelG(int x, int y, byte color)
        {
            Data[Stride * y + Stride / Width * x + 1] = color;
        }
        public void SetPixelR(int x, int y, byte color)
        {
            Data[Stride * y + Stride / Width * x + 2] = color;
        }

        public void SetPixel(int x, int y, byte R, byte G, byte B)
        {
            if (Stride / Width == 3)
            {
                var num = Stride * y + 3 * x;
                Data[num + 2] = R;
                Data[num + 1] = G;
                Data[num] = B;
                return;
            }
            if (Stride / Width == 1)
            {
                Data[Stride * y + Stride / Width * x] = B;
            }
        }

        public BitmapSobelEdge FromBitmap(Bitmap bitmap)
        {
            var result = new BitmapSobelEdge(bitmap.Clone() as Bitmap);
            return result;
        }
    }
}
