using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;


namespace CaptureGif
{
    /// <summary>
    /// RegionSelectScreenView.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelectScreenView : UserControl
    {
        public ScreenInfo Screen { get; }

        public Grid MainGrid { get; }

        public bool IsCurrent { get; private set; }

        private double _dpiScaleX => Screen.DpiScaleX;

        private double _dpiScaleY => Screen.DpiScaleY;

        private double _mainDpiScaleX => ScreenInfo.MainScreen.DpiScaleX;

        private double _mainDpiScaleY => ScreenInfo.MainScreen.DpiScaleY;

        private double _dpiScaleToMainX => _dpiScaleX / _mainDpiScaleX;

        private double _dpiScaleToMainY => _dpiScaleY / _mainDpiScaleY;

        private double _dpiScaleToBasicX => 1 / _dpiScaleX;

        private double _dpiScaleToBasicY => 1 / _dpiScaleY;

        private double _scaleX => _dpiScaleToBasicX * _dpiScaleToMainX;

        private double _scaleY => _dpiScaleToBasicY * _dpiScaleToMainY;

        private const int _forbidMarginTipGrid = 50;

        private static readonly string _tipModeString = "当前感知模式  {0}";

        public RegionSelectScreenView(ScreenInfo screen, Grid mainGrid)
        {
            InitializeComponent();
            Screen = screen;
            MainGrid = mainGrid;
            MainGrid.Children.Add(this);
            UpdateView();
        }

        public void UpdateView()
        {
            var dpiScaleX = _dpiScaleToBasicX * _dpiScaleToMainX;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(Screen.Bounds.Left * _scaleX, Screen.Bounds.Top * _scaleY, 0, 0);
            Width = Screen.Bounds.Width;
            Height = Screen.Bounds.Height;
            RenderTransform = new ScaleTransform(_scaleX, _scaleY);
        }

        public void UpdateScreenBitmap(BitmapSource source)
        {
            BackgroundImage.Source = source;
            BackgroundImageGrid.Width = Screen.Bounds.Width;
            BackgroundImageGrid.Height = Screen.Bounds.Height;
            BackgroundImage.Margin = new Thickness(-Screen.Bounds.Left, -Screen.Bounds.Top, 0, 0);
            RegionImage.Source = source;
        }

        public void UpdateMousePosition(DPoint point)
        {
            point = ConvertToScalePoint(point);
            IsCurrent = Screen.Bounds.Contains(point);
            if (IsCurrent)
            {
                TipGrid.Visibility = Visibility.Visible;
                if (point.X >= Screen.Bounds.Left + TipGrid.Margin.Left && point.Y >= Screen.Bounds.Top + TipGrid.Margin.Top
                    && point.X <= Screen.Bounds.Left + TipGrid.Margin.Left + TipGrid.ActualWidth + _forbidMarginTipGrid
                    && point.Y <= Screen.Bounds.Top + TipGrid.Margin.Top + TipGrid.ActualHeight + _forbidMarginTipGrid)
                {
                    TipGrid.VerticalAlignment = VerticalAlignment.Bottom;
                }
                else
                {
                    TipGrid.VerticalAlignment = VerticalAlignment.Top;
                }
            }
            else
            {
                TipGrid.Visibility = Visibility.Hidden;
            }
        }

        public void UpdatePerceptionPositionLine(int? x, int? y)
        {
            if (!IsCurrent)
            {
                LineHorizontal.Visibility = Visibility.Hidden;
                LineVertical.Visibility = Visibility.Hidden;
            }
            else
            {
                x = ConvertToScaleX(x);
                y = ConvertToScaleY(y);
                if (x.HasValue && x.Value >= Screen.Bounds.Left && x.Value <= Screen.Bounds.Right)
                {
                    LineVertical.X1 = x.Value - Screen.Bounds.Left;
                    LineVertical.X2 = x.Value - Screen.Bounds.Left;
                    LineVertical.Y1 = 0;
                    LineVertical.Y2 = Screen.Bounds.Height;
                    LineVertical.Visibility = Visibility.Visible;
                }
                else
                {
                    LineVertical.Visibility = Visibility.Hidden;
                }
                if (y.HasValue && y.Value >= Screen.Bounds.Top && y.Value <= Screen.Bounds.Bottom)
                {
                    LineHorizontal.X1 = 0;
                    LineHorizontal.X2 = Screen.Bounds.Width;
                    LineHorizontal.Y1 = y.Value - Screen.Bounds.Top;
                    LineHorizontal.Y2 = y.Value - Screen.Bounds.Top;
                    LineHorizontal.Visibility = Visibility.Visible;
                }
                else
                {
                    LineHorizontal.Visibility = Visibility.Hidden;
                }
            }
        }

        public void UpdateSelectedRegion(DRectangle rect)
        {
            rect = ConvertToScaleRectangle(rect);
            rect.Intersect(Screen.Bounds);
            if (rect == default)
            {
                RegionImageGrid.Visibility
                = LineRegionLeft.Visibility
                = LineRegionRight.Visibility
                = LineRegionBottom.Visibility
                = LineRegionTop.Visibility
                = Visibility.Hidden;
            }
            else
            {
                RegionImageGrid.Visibility = Visibility.Visible;
                RegionImageGrid.Margin = new Thickness(rect.X - Screen.Bounds.Left, rect.Y - Screen.Bounds.Top, 0, 0);
                RegionImageGrid.Width = rect.Width;
                RegionImageGrid.Height = rect.Height;
                RegionImage.Margin = new Thickness(-rect.X, -rect.Y, 0, 0);
                if (rect.X > Screen.Bounds.Left)
                {
                    LineRegionLeft.X1 = LineRegionLeft.X2 = rect.X - Screen.Bounds.Left;
                    LineRegionLeft.Y1 = rect.Y - Screen.Bounds.Top;
                    LineRegionLeft.Y2 = rect.Y - Screen.Bounds.Top + rect.Height;
                    LineRegionLeft.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionLeft.Visibility = Visibility.Hidden;
                }
                if (rect.X + rect.Width < Screen.Bounds.Right)
                {
                    LineRegionRight.X1 = LineRegionRight.X2 = rect.X - Screen.Bounds.Left + rect.Width;
                    LineRegionRight.Y1 = rect.Y - Screen.Bounds.Top;
                    LineRegionRight.Y2 = rect.Y - Screen.Bounds.Top + rect.Height;
                    LineRegionRight.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionRight.Visibility = Visibility.Hidden;
                }
                if (rect.Y + rect.Height < Screen.Bounds.Bottom)
                {
                    LineRegionBottom.X1 = rect.X - Screen.Bounds.Left;
                    LineRegionBottom.X2 = rect.X - Screen.Bounds.Left + rect.Width;
                    LineRegionBottom.Y1 = LineRegionBottom.Y2 = rect.Y - Screen.Bounds.Top + rect.Height;
                    LineRegionBottom.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionBottom.Visibility = Visibility.Hidden;
                }
                if (rect.Y > Screen.Bounds.Top)
                {
                    LineRegionTop.X1 = rect.X - Screen.Bounds.Left;
                    LineRegionTop.X2 = rect.X - Screen.Bounds.Left + rect.Width;
                    LineRegionTop.Y1 = LineRegionTop.Y2 = rect.Y - Screen.Bounds.Top;
                    LineRegionTop.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionTop.Visibility = Visibility.Hidden;
                }
            }
        }

        public void UpdateTipModeString(PerceptionMode mode)
        {
            switch (mode)
            {
                case PerceptionMode.None:
                    PerceptionTipLabel.Content = string.Format(_tipModeString, "无");
                    break;
                case PerceptionMode.Horizontal:
                    PerceptionTipLabel.Content = string.Format(_tipModeString, "水平");
                    break;
                case PerceptionMode.Vertical:
                    PerceptionTipLabel.Content = string.Format(_tipModeString, "垂直");
                    break;
                case PerceptionMode.Both:
                    PerceptionTipLabel.Content = string.Format(_tipModeString, "水平+竖直");
                    break;
            }
        }

        private DPoint ConvertToScalePoint(DPoint point)
        {
            return ConvertToScalePoint(point.X, point.Y);
        }

        private DPoint ConvertToScalePoint(double x, double y)
        {
            return new DPoint((int)(x / _scaleX), (int)(y / _scaleY));
        }

        private DRectangle ConvertToScaleRectangle(DRectangle rect)
        {
            return ConvertToScaleRectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private DRectangle ConvertToScaleRectangle(double x, double y, double width, double height)
        {
            return new DRectangle((int)(x / _scaleX), (int)(y / _scaleY), (int)(width / _scaleX), (int)(height / _scaleY));
        }

        private int? ConvertToScaleX(double? x)
        {
            if (x.HasValue)
            {
                return (int)(x / _scaleX);
            }
            return null;
        }

        private int? ConvertToScaleY(double? y)
        {
            if (y.HasValue)
            {
                return (int)(y / _scaleY);
            }
            return null;
        }
    }
}
