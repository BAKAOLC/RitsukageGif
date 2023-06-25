using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RitsukageGif
{
    public class BitmapSobelEdge
    {
        public int Width { get; }

        public int Height { get; }

        public Bitmap Bitmap { get; }

        public int Stride { get; }

        public Rectangle Rect { get; }

        public byte[] Data { get; }

        public byte[] DataBackup { get; }

        private BitmapSobelEdge(Bitmap bitmap)
        {
            Bitmap = bitmap;
            Width = Bitmap.Width;
            Height = Bitmap.Height;
            Rect = new Rectangle(0, 0, Width, Height);
            var data = Bitmap.LockBits(Rect, ImageLockMode.ReadWrite, Bitmap.PixelFormat);
            Stride = data.Stride;
            Data = new byte[Stride * Height];
            DataBackup = new byte[Stride * Height];
            Bitmap.UnlockBits(data);
        }

        public void ProcessToData(bool needBackup = false)
        {
            var data = Bitmap.LockBits(Rect, ImageLockMode.ReadWrite, Bitmap.PixelFormat);
            Marshal.Copy(data.Scan0, Data, 0, Data.Length);
            if (needBackup)
            {
                Marshal.Copy(data.Scan0, DataBackup, 0, DataBackup.Length);
            }

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
            switch (Stride / Width)
            {
                case 3:
                {
                    var num = Stride * y + 3 * x;
                    Data[num + 2] = R;
                    Data[num + 1] = G;
                    Data[num] = B;
                    return;
                }
                case 1:
                    Data[Stride * y + Stride / Width * x] = B;
                    break;
            }
        }

        public void ProcessToSingleRedChannel()
        {
            ProcessToData();
            for (int i = 0; i < Data.Length; i += 3)
            {
                Data[i] = Data[i + 1] = Data[i + 2];
            }

            ProcessToBitmap();
        }

        public void ProcessToSingleGreenChannel()
        {
            ProcessToData();
            for (int i = 0; i < Data.Length; i += 3)
            {
                Data[i] = Data[i + 2] = Data[i + 1];
            }

            ProcessToBitmap();
        }

        public void ProcessToSingleBlueChannel()
        {
            ProcessToData();
            for (int i = 0; i < Data.Length; i += 3)
            {
                Data[i + 1] = Data[i + 2] = Data[i];
            }

            ProcessToBitmap();
        }


        public void ProcessToGrey()
        {
            ProcessToData();
            for (int i = 0; i < Data.Length; i += 3)
            {
                Data[i] = Data[i + 2] = Data[i + 1] = (byte)(Data[i] + Data[i + 1] + Data[i + 2] / 3);
            }

            ProcessToBitmap();
        }

        public void ProcessSobelEdgeFilter()
        {
            ProcessToData(true);
            for (int i = 1; i < Width - 1; i++)
            {
                for (int j = 1; j < Height - 1; j++)
                {
                    int num = -SobelEdgeFilterGetValue(DataBackup, i - 1, j - 1) -
                              SobelEdgeFilterGetValue(DataBackup, i - 1, j) * 2 -
                              SobelEdgeFilterGetValue(DataBackup, i - 1, j + 1) +
                              SobelEdgeFilterGetValue(DataBackup, i + 1, j - 1) +
                              SobelEdgeFilterGetValue(DataBackup, i + 1, j) * 2 +
                              SobelEdgeFilterGetValue(DataBackup, i + 1, j + 1);
                    int num2 = -SobelEdgeFilterGetValue(DataBackup, i - 1, j - 1) -
                               SobelEdgeFilterGetValue(DataBackup, i, j - 1) * 2 -
                               SobelEdgeFilterGetValue(DataBackup, i + 1, j - 1) +
                               SobelEdgeFilterGetValue(DataBackup, i - 1, j + 1) +
                               SobelEdgeFilterGetValue(DataBackup, i, j + 1) * 2 +
                               SobelEdgeFilterGetValue(DataBackup, i + 1, j + 1);
                    int num3 = (int)Math.Sqrt(num * num + num2 * num2);
                    num3 = num3 > 255 ? 255 : num3;
                    SetPixel(i, j, (byte)num3, (byte)num3, (byte)num3);
                }
            }

            ProcessToBitmap();
        }

        public void ProcessThresholdFilter(byte threshold)
        {
            ProcessToData();
            for (int i = 0; i < Data.Length; i += 3)
            {
                Data[i] = Data[i + 1] = Data[i + 2] = Data[i] < threshold ? byte.MinValue : byte.MaxValue;
            }

            ProcessToBitmap();
        }

        public static BitmapSobelEdge FromBitmap(Bitmap bitmap)
        {
            var result = new BitmapSobelEdge(bitmap);
            return result;
        }

        private int SobelEdgeFilterGetValue(byte[] data, int x, int y)
        {
            return data[Stride * y + Stride / Width * x];
        }
    }
}