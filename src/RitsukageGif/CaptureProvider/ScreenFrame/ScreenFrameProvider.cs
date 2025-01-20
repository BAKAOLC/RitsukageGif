using System;
using System.Windows;
using SharpDX;
using SharpDX.DXGI;

namespace RitsukageGif.CaptureProvider.ScreenFrame
{
    public static class ScreenFrameProvider
    {
        public static IScreenFrameProvider CreateProvider(int provider)
        {
            IScreenFrameProvider screenFrameProvider;
            switch (provider)
            {
                case 0:
                    screenFrameProvider = new BitbltScreenFrameProvider();
                    break;
                case 1:
                    try
                    {
                        screenFrameProvider = new DxgiScreenFrameProvider();
                    }
                    catch (SharpDXException ex) when (ex.Descriptor == ResultCode.Unsupported ||
                                                      ex.Descriptor == ResultCode.NotCurrentlyAvailable)
                    {
                        const string errorMessage = "当前设备不支持 DXGI 桌面复制，有可能是由于显卡驱动不支持/当前并非使用独立显卡。"
                                                    + "\n请尝试使用 Bitblt 捕获方式。（取消勾选 \"启用DXGI(实验性)\" 选项）"
                                                    + "\n关闭本对话框后将自动切换到 Bitblt 捕获方式。"
                                                    + "\n注意：Bitblt 捕获方式可能会导致性能下降。";

                        MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        var mainWindow = MainWindow.GetInstance();
                        mainWindow?.Dispatcher.Invoke(() => { mainWindow.DXGIRecordCheckBox.IsChecked = false; });
                        screenFrameProvider = new BitbltScreenFrameProvider();
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(provider));
            }

            return screenFrameProvider;
        }
    }
}