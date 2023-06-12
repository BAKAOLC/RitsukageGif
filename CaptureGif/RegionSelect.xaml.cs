using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;
using FScreen = System.Windows.Forms.Screen;

namespace CaptureGif
{
    /// <summary>
    /// RegionSelect.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelect : Window
    {
        private PerceptionMode _perceptionMode = PerceptionMode.None;

        private bool _selected;
        private DPoint _selectedStartPoint;
        private DPoint _selectedEndPoint;
        private bool _selecting;
        private DPoint _selectingStartPoint;
        private DPoint _selectingEndPoint;
        private DRectangle SelectedRange
        {
            get
            {
                return _selected ? new DRectangle()
                {
                    X = Math.Min(_selectedStartPoint.X, _selectedEndPoint.X),
                    Y = Math.Min(_selectedStartPoint.Y, _selectedEndPoint.Y),
                    Width = Math.Abs(_selectedStartPoint.X - _selectedEndPoint.X),
                    Height = Math.Abs(_selectedStartPoint.Y - _selectedEndPoint.Y)
                } : default;
            }
        }
        private DRectangle SelectingRange
        {
            get
            {
                return _selecting ? new DRectangle()
                {
                    X = Math.Min(_selectingStartPoint.X, _selectingEndPoint.X),
                    Y = Math.Min(_selectingStartPoint.Y, _selectingEndPoint.Y),
                    Width = Math.Abs(_selectingStartPoint.X - _selectingEndPoint.X),
                    Height = Math.Abs(_selectingStartPoint.Y - _selectingEndPoint.Y)
                } : default;
            }
        }

        private ScreenInfo[] _allScreens;
        private RegionSelectScreenView[] _allScreenViewGrids;
        private Bitmap _screenBitmap;
        private BitmapSource _screenBitmapSource;

        private RegionSelect()
        {
            InitializeComponent();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = false;
        }

        public Task<(bool, SelectedRegionResult)> WaitForResult()
        {
            var tcs = new TaskCompletionSource<(bool, SelectedRegionResult)>();
            var timer = new Timer(100);
            timer.Elapsed += (s, e) =>
            {
                if (_closing && _ok)
                {
                    timer.Stop();
                    var rect = SelectedRange;
                    var regions = _allScreens.Select(x => new ScreenRegion(x, x.GetConvertedIntersectionRegion(rect)))
                        .Where(x => x.Rectangle != default)
                        .ToArray();
                    var result = new SelectedRegionResult(rect, regions);
                    if (_confirm && rect != default)
                    {
                        tcs.SetResult((_confirm, result));
                    }
                    else
                    {
                        tcs.SetResult((_confirm, null));
                    }
                }
            };
            timer.Start();
            return tcs.Task;
        }

        public static RegionSelect Begin()
        {
            var regionSelect = new RegionSelect();
            regionSelect.Show();
            return regionSelect;
        }

        private void Initialize()
        {
            UpdateSizeToScreenSize();
            UpdateScreenBitmap();
            UpdateSelectedRegion(false);
        }

        private void UpdateScreens()
        {
            _allScreens = FScreen.AllScreens.Select(x =>
            {
                var exist = _allScreens?.FirstOrDefault(y => y.Screen == x);
                if (exist == null)
                {
                    exist = ScreenInfo.GetScreenInfo(x);
                }
                else
                {
                    exist.UpdateDpiScale();
                }
                return exist;
            })?.ToArray();
            _allScreenViewGrids = _allScreens.Select(x =>
            {
                var exist = _allScreenViewGrids?.FirstOrDefault(y => y.Screen == x);
                if (exist == null)
                {
                    exist = new RegionSelectScreenView(x, MainGrid);
                }
                else
                {
                    exist.UpdateView();
                }
                exist.UpdateTipModeString(_perceptionMode);
                return exist;
            })?.ToArray();
        }

        private (int, int) GetScreenFullSize()
        {
            UpdateScreens();
            var width = 0;
            var height = 0;
            foreach (var screen in _allScreens)
            {
                width = Math.Max(width, screen.Bounds.Right);
                height = Math.Max(height, screen.Bounds.Bottom);
            }
            return (width, height);
        }

        private void UpdateSizeToScreenSize()
        {
            (var width, var height) = GetScreenFullSize();
            Left = 0;
            Top = 0;
            MainGrid.Width = Width = width;
            MainGrid.Height = Height = height;
        }

        private void UpdateScreenBitmap()
        {
            (var width, var height) = GetScreenFullSize();
            _screenBitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(_screenBitmap))
            {
                foreach (var screen in _allScreens)
                {
                    graphics.CopyFromScreen(screen.Bounds.Location, screen.Bounds.Location, screen.Bounds.Size);
                }
            }
            _screenBitmapSource = _screenBitmap.ToBitmapSource();
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateScreenBitmap(_screenBitmapSource);
            }
        }

        private void UpdateSelectedRegion(bool isSelecting = false)
        {
            DRectangle rect = isSelecting ? SelectingRange : SelectedRange;
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateSelectedRegion(rect);
            }
        }

        private bool _closing;
        private bool _confirm;
        private bool _ok;

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ok = true;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_closing) return;
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _selecting = true;
                var position = e.GetPosition(this);
                var point = new DPoint((int)position.X, (int)position.Y);
                _selectingStartPoint = point;
                _selectingEndPoint = point;
            }
            if (e.RightButton == MouseButtonState.Pressed)
            {
                _closing = true;
                _confirm = true;
                Close();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_closing) return;

            var position = e.GetPosition(this);
            var point = new DPoint((int)position.X, (int)position.Y);
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateMousePosition(point);
                view.UpdatePerceptionPositionLine(point.X, point.Y);
            }

            if (_selecting)
            {
                _selectingEndPoint = point;
                UpdateSelectedRegion(true);
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_closing) return;
            if (_selecting)
            {
                if (e.LeftButton == MouseButtonState.Released)
                {
                    _selecting = false;
                    _selected = true;
                    var position = e.GetPosition(this);
                    _selectingEndPoint = new DPoint((int)position.X, (int)position.Y);
                    _selectedStartPoint = _selectingStartPoint;
                    _selectedEndPoint = _selectingEndPoint;
                    UpdateSelectedRegion(false);
                }
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_closing) return;
            switch (e.Key)
            {
                case Key.Escape:
                    if (_selecting)
                    {
                        _selecting = false;
                        UpdateSelectedRegion(false);
                    }
                    else if (_selected)
                    {
                        _selected = false;
                        UpdateSelectedRegion(false);
                    }
                    else
                    {
                        _closing = true;
                        Close();
                    }
                    break;
                case Key.LeftShift:
                case Key.RightShift:
                    break;
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if (_closing) return;
        }

        private void Window_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_closing) return;
            Close();
        }

        private void Window_DpiChanged(object sender, DpiChangedEventArgs e)
        {
            if (_closing) return;
        }
    }
}
