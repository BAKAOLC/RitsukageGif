using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace RitsukageGif
{
    /// <summary>
    /// AboutWindow.xaml 的交互逻辑
    /// </summary>
    public partial class AboutWindow : Window
    {
        private static AboutWindow _instance = null;
        private bool _closeFlag = false;

        private static readonly string AboutText = $@"RitsukageGif ver {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}
构建日期: {System.IO.File.GetLastWriteTime(typeof(AboutWindow).Assembly.Location)}

◆关于本软件
原型设计来自于 Seiwell 设计制作的 SlimeGif
基于小炎酱的 SlimeGif ver 3.3 / 改 V3.61 进行功能设计
由于其实现过于老旧，不支持DPI适配，同时在高分辨率屏幕下选择区域会因为错误的实现导致十分严重的卡顿
而且原始设计缺少维护，因此使用 WPF 进行重新设计制作

◆操作说明 - 录制区域
△由于不提供默认录制区域，请先通过 “选择区域” 按钮进行调整
△在选择区域时，程序会自动识别窗体范围并进行选取
△按下并拖动鼠标左键，进行选区的设置与调整
△拖动选择区域时，会自动根据背景颜色进行边缘感知（可通过点击 SHIFT 键切换感知模式，或长按 SHIFT 键屏蔽感知）
△使用鼠标右键确认选区（注：未选择任何像素时，会自动设置为全屏范围）
△使用 ESC 键可以取消选区（注：如果没有选区，则会取消区域设置操作）

◆操作说明 - 录制
△录制过程中，会实时显示录制帧数（注：显示格式为 当前已编码的帧数 / 当前已录制的帧数）
△在 GIF 编码完毕后，会自动在窗体内显示预览，展示 GIF 文件的大小，并将 GIF 文件的预览复制至剪切板中
△文件大小在大于 3MB 的时候将会标注为红色
△文件大小在大于 6MB 的时候将会标注为深红色，并加粗显示
△通过鼠标左键按住预览图像并拖动可以复制图像到外部路径
△通过鼠标右键单机预览图像可以再次复制图像到剪切板

◆操作说明 - 选项
△缩小倍率 —— 使用 (图像大小 / 倍率) 作为录制图像大小
△帧率 —— 每秒录制的帧数
　帧率过高可能会导致在某些程序中无法正确显示速率
　受录制与编码的影响，实际帧率可能会低于设定值
△录制鼠标指针 —— 在录制的图像中包含鼠标指针
△使用内存录制 —— 录制帧先存进内存，使用额外线程进行编码，以减少编码导致的帧率波动
　使用该功能会导致程序使用内存大幅增加，请谨慎使用

◆快捷键
△开始/结束录制  CTRL+SHIFT+A
△设置录制区域   CTRL+SHIFT+S

◆重置版说明
[v1.0.0.2]
△重新设计主界面，程序图标多尺寸适配
[v1.0.0.1]
△修复本窗口“确定”按钮无效的的问题
[v1.0.0.0]
△增加了 DPI 感知，并处理了多个屏幕使用不同 DPI 时的显示问题
△使用 WPF 完全重新设计，基于 WPF 事件完全重新设计区域选择界面
△增加了鼠标指针录制功能，该设置会自动保存
△增加了内存录制功能，该设置会自动保存
△修改了默认配置帧率，以减少 QQ 的 GIF 速率显示问题

◆使用的扩展依赖
△Animated Gif
△WPF Animated Gif
△Extended WPF Toolkit
△NHotkey WPF
△Costura Fody";

        public AboutWindow()
        {
            _instance = this;
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AboutTextBox.Text = AboutText;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closeFlag) return;
            e.Cancel = true;
            AboutTextBox.ScrollToHome();
            Hide();
        }

        private static AboutWindow GetInstance()
        {
            return _instance ?? (_instance = new AboutWindow());
        }

        public static AboutWindow Begin()
        {
            var instance = GetInstance();
            instance.Show();
            return instance;
        }

        public static void CloseInstance()
        {
            if (_instance == null) return;
            _instance._closeFlag = true;
            _instance.Close();
            _instance = null;
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
