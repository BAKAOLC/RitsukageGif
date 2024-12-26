using System;
using System.Drawing;
using System.Drawing.Imaging;
using RitsukageGif.Native;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using D3D11Device = SharpDX.Direct3D11.Device;
using D3D11MapFlags = SharpDX.Direct3D11.MapFlags;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public class DXGIScreenFrameProvider : IScreenFrameProvider
    {
        public const string ProviderId = "71df2757-2b99-b209-f74d-bdb55d448938";
        private Adapter1 _adapter;
        private D3D11Device _device;

        private bool _disposed;
        private DuplicatedOutput[] _duplicatedOutputs;

        private Factory1 _factory;

        private Rectangle _rectangle;

        public DXGIScreenFrameProvider()
        {
            Initialize();
        }

        public void Initialize()
        {
            _factory = new();
            _adapter = _factory.GetAdapter1(0);
            _device = new(_adapter);
            var count = _adapter.GetOutputCount();
            _duplicatedOutputs = new DuplicatedOutput[count];
            for (var i = 0; i < count; i++)
                _duplicatedOutputs[i] = new(_adapter.GetOutput(i), _device);
        }

        public void ApplyCaptureRegion(Rectangle rect)
        {
            _rectangle = rect;
        }

        public Bitmap Capture(bool cursor = false, double scale = 1)
        {
            var width = (int)(_rectangle.Width * scale);
            var height = (int)(_rectangle.Height * scale);
            var bitmap = new Bitmap(width, height);
            using var source = new Bitmap(_rectangle.Width, _rectangle.Height);
            using var graphicsBitmap = Graphics.FromImage(bitmap);
            using var graphicsSource = Graphics.FromImage(source);
            //graphicsSource.CopyFromScreen(_rectangle.X, _rectangle.Y, 0, 0, _rectangle.Size);
            foreach (var duplicatedOutput in _duplicatedOutputs)
            {
                var desc = duplicatedOutput.Output.Description;
                var screenRect = new Rectangle(desc.DesktopBounds.Left, desc.DesktopBounds.Top,
                    desc.DesktopBounds.Right - desc.DesktopBounds.Left,
                    desc.DesktopBounds.Bottom - desc.DesktopBounds.Top);
                var rect = Rectangle.Intersect(_rectangle, screenRect);
                if (rect == default)
                    continue;
                var graphicRect = new Rectangle(rect.X - _rectangle.X, rect.Y - _rectangle.Y, rect.Width,
                    rect.Height);
                var desktopGraphicRect = new Rectangle(rect.X - screenRect.X, rect.Y - screenRect.Y, rect.Width,
                    rect.Height);
                using var screen = duplicatedOutput.Capture();
                if (screen == null) continue;
                graphicsSource.DrawImage(screen, graphicRect, desktopGraphicRect, GraphicsUnit.Pixel);
            }

            if (cursor) Cursor.Draw(graphicsSource, p => new(p.X - _rectangle.X, p.Y - _rectangle.Y));

            graphicsBitmap.DrawImage(source, 0, 0, width, height);

            return bitmap;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~DXGIScreenFrameProvider()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                foreach (var duplicatedOutput in _duplicatedOutputs) duplicatedOutput.Dispose();
                _device?.Dispose();
                _adapter?.Dispose();
                _factory?.Dispose();
            }

            _disposed = true;
        }

        private sealed class DuplicatedOutput : IDisposable
        {
            private readonly D3D11Device _device;
            private readonly Output1 _output1;
            private readonly OutputDuplication _outputDuplication;
            private readonly Texture2DDescription _textureDesc;
            public readonly Output Output;

            private bool _disposed;

            public DuplicatedOutput(Output output, D3D11Device device)
            {
                Output = output;
                _device = device;
                _output1 = output.QueryInterface<Output1>();
                _outputDuplication = _output1.DuplicateOutput(device);
                var x = Output.Description.DesktopBounds.Left;
                var y = Output.Description.DesktopBounds.Top;
                var width = Output.Description.DesktopBounds.Right - x;
                var height = Output.Description.DesktopBounds.Bottom - y;
                _textureDesc = new()
                {
                    CpuAccessFlags = CpuAccessFlags.Read,
                    BindFlags = BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = width,
                    Height = height,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = ResourceUsage.Staging,
                };
                // 丢弃前两帧
                for (var i = 0; i < 2; i++)
                {
                    _outputDuplication.AcquireNextFrame(1000, out _, out _);
                    _outputDuplication.ReleaseFrame();
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            ~DuplicatedOutput()
            {
                Dispose(false);
            }

            public Bitmap Capture()
            {
                using var screenTexture = new Texture2D(_device, _textureDesc);
                if (!_outputDuplication.TryAcquireNextFrame(1000, out _, out var screenResource).Success)
                    return null;
                using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
                {
                    _device.ImmediateContext.CopyResource(screenTexture2D, screenTexture);
                }

                var mapSource = _device.ImmediateContext.MapSubresource(screenTexture, 0,
                    MapMode.Read, D3D11MapFlags.None);
                var bitmap = new Bitmap(_textureDesc.Width, _textureDesc.Height, PixelFormat.Format32bppArgb);
                var boundsRect = new Rectangle(0, 0, _textureDesc.Width, _textureDesc.Height);
                var mapDest = bitmap.LockBits(boundsRect, ImageLockMode.WriteOnly,
                    bitmap.PixelFormat);
                var sourcePtr = mapSource.DataPointer;
                var destPtr = mapDest.Scan0;
                for (var l = 0; l < _textureDesc.Height; l++)
                {
                    Utilities.CopyMemory(destPtr, sourcePtr, _textureDesc.Width * 4);
                    sourcePtr = IntPtr.Add(sourcePtr, mapSource.RowPitch);
                    destPtr = IntPtr.Add(destPtr, mapDest.Stride);
                }

                bitmap.UnlockBits(mapDest);
                _device.ImmediateContext.UnmapSubresource(screenTexture, 0);
                _outputDuplication.ReleaseFrame();
                return bitmap;
            }

            private void Dispose(bool disposing)
            {
                if (_disposed) return;
                if (disposing)
                {
                    _outputDuplication.Dispose();
                    _output1.Dispose();
                    Output.Dispose();
                }

                _disposed = true;
            }
        }
    }
}