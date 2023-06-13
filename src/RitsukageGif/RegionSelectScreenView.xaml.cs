using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;


namespace RitsukageGif
{
    /// <summary>
    /// RegionSelectScreenView.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelectScreenView : UserControl
    {
        public RegionSelect Main { get; }

        public Grid MainGrid { get; }

        public ScreenInfo Screen { get; }

        public bool IsCurrent { get; private set; }

        private PerceptionMode PerceptionMode => Main.PerceptionMode;

        private const int _forbidMarginTipGrid = 50;

        private static readonly string _tipModeString = "当前感知模式  {0}";

        public RegionSelectScreenView(RegionSelect main, Grid mainGrid, ScreenInfo screen)
        {
            InitializeComponent();
            Main = main;
            Screen = screen;
            MainGrid = mainGrid;
            MainGrid.Children.Add(this);
            UpdateView();
        }

        public void UpdateView()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(Screen.Bounds.Left * Screen.ConvertScaleX, Screen.Bounds.Top * Screen.ConvertScaleY, 0, 0);
            Width = Screen.Bounds.Width;
            Height = Screen.Bounds.Height;
            RenderTransform = new ScaleTransform(Screen.ConvertScaleX, Screen.ConvertScaleY);
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
            var pointF = Screen.ConvertToScalePoint(point);
            IsCurrent = Screen.Bounds.Contains(pointF);
            if (IsCurrent)
            {
                UpdatePerceptionMode();
                TipGrid.Visibility = Visibility.Visible;
                if (pointF.X >= Screen.Bounds.Left + TipGrid.Margin.Left && pointF.Y >= Screen.Bounds.Top + TipGrid.Margin.Top
                    && pointF.X <= Screen.Bounds.Left + TipGrid.Margin.Left + TipGrid.ActualWidth + _forbidMarginTipGrid
                    && pointF.Y <= Screen.Bounds.Top + TipGrid.Margin.Top + TipGrid.ActualHeight + _forbidMarginTipGrid)
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

        public DPoint UpdatePerceptionPosition(int x, int y)
        {
            if (!IsCurrent)
            {
                LineHorizontal.Visibility = Visibility.Hidden;
                LineVertical.Visibility = Visibility.Hidden;
            }
            else
            {
                Main.ProcessPerceptionProgramArea(x, y);
                var sx = Screen.ConvertToScaleX(x);
                var sy = Screen.ConvertToScaleY(y);
                bool horizontal = PerceptionMode == PerceptionMode.Horizontal || PerceptionMode == PerceptionMode.Both;
                bool vertical = PerceptionMode == PerceptionMode.Vertical || PerceptionMode == PerceptionMode.Both;
                Main.ProcessPerceptionNearPoint(new DPoint((int)sx, (int)sy), horizontal, vertical);
                if (Main.PerceptionImagePoint != Main.InvalidPerceptionPoint)
                {
                    int px = Main.PerceptionImagePoint.X;
                    int py = Main.PerceptionImagePoint.Y;
                    bool fx = false;
                    bool fy = false;
                    if (px != -1 && px >= Screen.Bounds.Left && px <= Screen.Bounds.Right)
                    {
                        LineVertical.X1 = px - Screen.Bounds.Left;
                        LineVertical.X2 = px - Screen.Bounds.Left;
                        LineVertical.Y1 = 0;
                        LineVertical.Y2 = Screen.Bounds.Height;
                        LineVertical.Visibility = Visibility.Visible;
                        fx = true;
                    }
                    else
                    {
                        LineVertical.Visibility = Visibility.Hidden;
                    }
                    if (py != -1 && py >= Screen.Bounds.Top && py <= Screen.Bounds.Bottom)
                    {
                        LineHorizontal.X1 = 0;
                        LineHorizontal.X2 = Screen.Bounds.Width;
                        LineHorizontal.Y1 = py - Screen.Bounds.Top;
                        LineHorizontal.Y2 = py - Screen.Bounds.Top;
                        LineHorizontal.Visibility = Visibility.Visible;
                        fy = true;
                    }
                    else
                    {
                        LineHorizontal.Visibility = Visibility.Hidden;
                    }
                    if (fx || fy)
                        return new DPoint(fx ? Screen.ConvertFromScaleX(px) : -1, fy ? Screen.ConvertFromScaleY(py) : -1);
                }
                else
                {
                    LineVertical.Visibility = Visibility.Hidden;
                    LineHorizontal.Visibility = Visibility.Hidden;
                }
            }
            return Main.InvalidPerceptionPoint;
        }

        public void UpdateSelectedRegion(DRectangle rect)
        {
            var rectF = Screen.GetConvertedIntersectionRegion(rect);
            if (rectF == default)
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
                RegionImageGrid.Margin = new Thickness(rectF.X - Screen.Bounds.Left, rectF.Y - Screen.Bounds.Top, 0, 0);
                RegionImageGrid.Width = rectF.Width;
                RegionImageGrid.Height = rectF.Height;
                RegionImage.Margin = new Thickness(-rectF.X, -rectF.Y, 0, 0);
                if (rectF.X > Screen.Bounds.Left)
                {
                    LineRegionLeft.X1 = LineRegionLeft.X2 = rectF.X - Screen.Bounds.Left;
                    LineRegionLeft.Y1 = rectF.Y - Screen.Bounds.Top;
                    LineRegionLeft.Y2 = rectF.Y - Screen.Bounds.Top + rectF.Height;
                    LineRegionLeft.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionLeft.Visibility = Visibility.Hidden;
                }
                if (rectF.X + rectF.Width < Screen.Bounds.Right)
                {
                    LineRegionRight.X1 = LineRegionRight.X2 = rectF.X - Screen.Bounds.Left + rectF.Width;
                    LineRegionRight.Y1 = rectF.Y - Screen.Bounds.Top;
                    LineRegionRight.Y2 = rectF.Y - Screen.Bounds.Top + rectF.Height;
                    LineRegionRight.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionRight.Visibility = Visibility.Hidden;
                }
                if (rectF.Y + rectF.Height < Screen.Bounds.Bottom)
                {
                    LineRegionBottom.X1 = rectF.X - Screen.Bounds.Left;
                    LineRegionBottom.X2 = rectF.X - Screen.Bounds.Left + rectF.Width;
                    LineRegionBottom.Y1 = LineRegionBottom.Y2 = rectF.Y - Screen.Bounds.Top + rectF.Height;
                    LineRegionBottom.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionBottom.Visibility = Visibility.Hidden;
                }
                if (rectF.Y > Screen.Bounds.Top)
                {
                    LineRegionTop.X1 = rectF.X - Screen.Bounds.Left;
                    LineRegionTop.X2 = rectF.X - Screen.Bounds.Left + rectF.Width;
                    LineRegionTop.Y1 = LineRegionTop.Y2 = rectF.Y - Screen.Bounds.Top;
                    LineRegionTop.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionTop.Visibility = Visibility.Hidden;
                }
            }
        }

        public void UpdatePerceptionMode()
        {
            switch (PerceptionMode)
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
    }
}
