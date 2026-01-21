using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RitsukageGif.Class;
using RitsukageGif.Enums;
using RitsukageGif.Extensions;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;

namespace RitsukageGif.Windows
{
    /// <summary>
    ///     RegionSelectScreenView.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelectScreenView
    {
        private const int TipGridWidth = 300;
        private const int TipGridHeight = 120;
        private const int TipGridExpandMargin = 50;
        private const int TipGridLabelFontSize = 12;
        private const int TipGridLabelMargin = 5;
        private const int TipGridLabelMargin2 = 20;
        private const string TipGridModeString = "当前感知模式  {0}";

        private double _tipGridScale = 1;

        public RegionSelectScreenView(RegionSelect main, Grid mainGrid, ScreenInfo screen)
        {
            InitializeComponent();
            Main = main;
            Screen = screen;
            MainGrid = mainGrid;
            MainGrid.Children.Add(this);
            UpdateView();
        }

        public static DPoint InvalidPoint => RegionSelect.InvalidPoint;

        public RegionSelect Main { get; }

        public Grid MainGrid { get; }

        public ScreenInfo Screen { get; }

        public bool IsCurrent { get; private set; }

        private PerceptionMode PerceptionMode => Main.PerceptionMode;

        public void UpdateView()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            var x = Screen.Bounds.Left * Screen.ConvertScaleX - Main.ScreenRectangle.Left;
            var y = Screen.Bounds.Top * Screen.ConvertScaleY - Main.ScreenRectangle.Top;
            Margin = new(x, y, 0, 0);
            Width = Screen.Bounds.Width;
            Height = Screen.Bounds.Height;
            RenderTransform = new ScaleTransform(Screen.ConvertScaleX, Screen.ConvertScaleY);
            UpdateTipGridScale();
        }

        public void UpdateScreenBitmap(BitmapSource source)
        {
            BackgroundImage.Source = source;
            BackgroundImageGrid.Width = Screen.Bounds.Width;
            BackgroundImageGrid.Height = Screen.Bounds.Height;
            var x = -Screen.Bounds.Left + Main.ScreenRectangle.Left;
            var y = -Screen.Bounds.Top + Main.ScreenRectangle.Top;
            BackgroundImage.Margin = new(x, y, 0, 0);
            RegionImage.Source = source;
        }

        public void UpdateTipGridScale()
        {
            var scaleX = Width / 1280;
            var scaleY = Height / 720;
            var scale = scaleX < scaleY ? scaleX : scaleY;
            scale = (scale - 1) / 2 + 1;
            scale = (int)(scale * 4 + 0.5) / 4.0;

            var y = TipGridLabelMargin * scale;
            _tipGridScale = scale;
            TipGrid.Width = TipGridWidth * scale;
            TipGrid.Height = TipGridHeight * scale;
            TipGridLabel1.FontSize = TipGridLabelFontSize * scale;
            TipGridLabel1.Margin = new(TipGridLabelMargin * scale, y, 0, 0);
            y += TipGridLabelMargin2 * scale;
            TipGridLabel2.FontSize = TipGridLabelFontSize * scale;
            TipGridLabel2.Margin = new(TipGridLabelMargin * scale, y, 0, 0);
            y += TipGridLabelMargin2 * scale;
            TipGridLabel3.FontSize = TipGridLabelFontSize * scale;
            TipGridLabel3.Margin = new(TipGridLabelMargin * scale, y, 0, 0);
            y += TipGridLabelMargin2 * scale;
            TipGridLabel4.FontSize = TipGridLabelFontSize * scale;
            TipGridLabel4.Margin = new(TipGridLabelMargin * scale, y, 0, 0);
            y += TipGridLabelMargin2 * scale;
            PerceptionTipLabel.FontSize = TipGridLabelFontSize * scale;
            PerceptionTipLabel.Margin = new(TipGridLabelMargin * scale, y, 0, 0);
        }

        public void UpdateMousePosition(DPoint point)
        {
            var pointF = Screen.ConvertToScalePoint(point);
            IsCurrent = Screen.Bounds.Contains(pointF);
            if (IsCurrent)
            {
                UpdatePerceptionMode();
                TipGrid.Visibility = Visibility.Visible;
                TipGrid.VerticalAlignment = pointF.X >= Screen.Bounds.Left && pointF.X <= Screen.Bounds.Left +
                                                                           TipGrid.Margin.Left +
                                                                           TipGrid.ActualWidth +
                                                                           TipGridExpandMargin * _tipGridScale
                                                                           && pointF.Y >= Screen.Bounds.Top &&
                                                                           pointF.Y <= Screen.Bounds.Top +
                                                                           TipGrid.Margin.Top
                                                                           + TipGrid.ActualHeight +
                                                                           TipGridExpandMargin * _tipGridScale
                    ? VerticalAlignment.Bottom
                    : VerticalAlignment.Top;
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
                var sx = Screen.ConvertToScaleX(x);
                var sy = Screen.ConvertToScaleY(y);
                var sp = new DPoint((int)sx, (int)sy);
                Main.ProcessPerceptionProgramArea(sp);
                var horizontal = PerceptionMode == PerceptionMode.Horizontal || PerceptionMode == PerceptionMode.Both;
                var vertical = PerceptionMode == PerceptionMode.Vertical || PerceptionMode == PerceptionMode.Both;
                Main.ProcessPerceptionNearPoint(sp, horizontal, vertical);
                if (Main.PerceptionImagePoint != InvalidPoint)
                {
                    var px = Main.PerceptionImagePoint.X;
                    var py = Main.PerceptionImagePoint.Y;
                    var fx = false;
                    var fy = false;
                    if (px != InvalidPoint.X && px >= Screen.Bounds.Left && px <= Screen.Bounds.Right)
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

                    if (py != InvalidPoint.Y && py >= Screen.Bounds.Top && py <= Screen.Bounds.Bottom)
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
                        return new(fx ? Screen.ConvertFromScaleX(px) : InvalidPoint.X,
                            fy ? Screen.ConvertFromScaleY(py) : InvalidPoint.Y);
                }
                else
                {
                    LineVertical.Visibility = Visibility.Hidden;
                    LineHorizontal.Visibility = Visibility.Hidden;
                }
            }

            return InvalidPoint;
        }

        public void UpdateSelectedRegion(DRectangle rect, bool needConvert = true, double opacity = 1)
        {
            var rectF = Screen.GetConvertedIntersectionRegion(rect, needConvert);
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
                RegionImageGrid.Opacity = opacity;
                var x = rectF.X - Screen.Bounds.Left;
                var y = rectF.Y - Screen.Bounds.Top;
                RegionImageGrid.Margin = new(x, y, 0, 0);
                RegionImageGrid.Width = rectF.Width;
                RegionImageGrid.Height = rectF.Height;
                RegionImage.Margin = new(-rectF.X + Main.ScreenRectangle.Left, -rectF.Y +
                                                                               Main.ScreenRectangle.Top, 0, 0);
                if (rectF.X > Screen.Bounds.Left)
                {
                    LineRegionLeft.X1 = LineRegionLeft.X2 = x;
                    LineRegionLeft.Y1 = y;
                    LineRegionLeft.Y2 = y + rectF.Height;
                    LineRegionLeft.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionLeft.Visibility = Visibility.Hidden;
                }

                if (rectF.X + rectF.Width < Screen.Bounds.Right)
                {
                    LineRegionRight.X1 = LineRegionRight.X2 = x + rectF.Width;
                    LineRegionRight.Y1 = y;
                    LineRegionRight.Y2 = y + rectF.Height;
                    LineRegionRight.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionRight.Visibility = Visibility.Hidden;
                }

                if (rectF.Y + rectF.Height < Screen.Bounds.Bottom)
                {
                    LineRegionBottom.X1 = x;
                    LineRegionBottom.X2 = x + rectF.Width;
                    LineRegionBottom.Y1 = LineRegionBottom.Y2 = y + rectF.Height;
                    LineRegionBottom.Visibility = Visibility.Visible;
                }
                else
                {
                    LineRegionBottom.Visibility = Visibility.Hidden;
                }

                if (rectF.Y > Screen.Bounds.Top)
                {
                    LineRegionTop.X1 = x;
                    LineRegionTop.X2 = x + rectF.Width;
                    LineRegionTop.Y1 = LineRegionTop.Y2 = y;
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
            PerceptionTipLabel.Content = PerceptionMode switch
            {
                PerceptionMode.None => string.Format(TipGridModeString, "无"),
                PerceptionMode.Horizontal => string.Format(TipGridModeString, "水平"),
                PerceptionMode.Vertical => string.Format(TipGridModeString, "垂直"),
                PerceptionMode.Both => string.Format(TipGridModeString, "水平+竖直"),
                _ => PerceptionTipLabel.Content,
            };
        }
    }
}