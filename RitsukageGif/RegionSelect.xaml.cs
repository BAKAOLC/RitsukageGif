using RitsukageGif.Native;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;
using FScreen = System.Windows.Forms.Screen;
using WPoint = System.Windows.Point;

namespace RitsukageGif
{
    /// <summary>
    /// RegionSelect.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelect : Window
    {
        public readonly DPoint InvalidPerceptionPoint = new DPoint(-1, -1);

        public DPoint PerceptionImagePoint { get; private set; }

        public DRectangle PerceptionProgramArea { get; private set; }

        private PerceptionMode _perceptionMode = PerceptionMode.Both;
        public PerceptionMode PerceptionMode
        {
            get
            {
                return _shiftKey ? PerceptionMode.None : _perceptionMode;
            }
            set
            {
                _perceptionMode = value;
            }
        }

        private bool _selected;
        private DPoint _selectedStartPoint;
        private DPoint _selectedEndPoint;
        private DRectangle SelectedRange
        {
            get
            {
                return _selected
                    ? new DRectangle()
                    {
                        X = Math.Min(_selectedStartPoint.X, _selectedEndPoint.X),
                        Y = Math.Min(_selectedStartPoint.Y, _selectedEndPoint.Y),
                        Width = Math.Abs(_selectedStartPoint.X - _selectedEndPoint.X),
                        Height = Math.Abs(_selectedStartPoint.Y - _selectedEndPoint.Y)
                    }
                    : default;
            }
        }

        private bool _selecting;
        private DPoint _selectingStartPoint;
        private DPoint _selectingEndPoint;
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
        private Bitmap _screenBitmapForSobelEdge;
        private BitmapSource _screenBitmapSource;
        private DPoint _mousePositionForScreen;
        private DPoint _perceptionPoint;

        private bool _shiftKey;
        private bool _shiftKeyHolding;
        private readonly Timer _shiftKeyTimer = new Timer(500);

        private const int EdgeCheckWidth = 60;
        private const int EdgeCheckHeight = 60;
        private const int EdgeCheckOffset = 25;
        private const int EdgeCheckThreshold = 100;

        private RegionSelect()
        {
            InitializeComponent();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            _shiftKeyTimer.Elapsed += (s, e) =>
            {
                _shiftKeyHolding = true;
                _shiftKeyTimer.Stop();
            };
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

        public void ProcessPerceptionProgramArea(int x, int y)
        {
            IsEnabled = false;
            var pt = new WPoint(x, y);
            var intPtr = User32.ChildWindowFromPointEx(User32.GetDesktopWindow(), pt, 3U);
            if (intPtr != IntPtr.Zero)
            {
                var intPtr2 = intPtr;
                for (; ; )
                {
                    User32.ScreenToClient(intPtr2, out pt);
                    intPtr2 = User32.ChildWindowFromPointEx(intPtr2, pt, 0U);
                    if (intPtr2 == IntPtr.Zero || intPtr2 == intPtr)
                    {
                        break;
                    }
                    intPtr = intPtr2;
                    pt.X = x;
                    pt.Y = y;
                }
                User32.GetWindowRect(intPtr, out var rect);
                PerceptionProgramArea = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            IsEnabled = true;
        }

        public void ProcessPerceptionNearPoint(DPoint MousePoint, bool horizontal, bool vertical)
        {
            if (_screenBitmapForSobelEdge == null || (!(horizontal || vertical)))
            {
                PerceptionImagePoint = InvalidPerceptionPoint;
                return;
            }
            var X = -1;
            var Y = -1;
            var empty = DPoint.Empty;
            var totalWidth = _screenBitmap.Width;
            var totalHeight = _screenBitmap.Height;
            if (MousePoint.X - EdgeCheckWidth / 2 < 0)
            {
                empty.X = 0;
            }
            else if (MousePoint.X + EdgeCheckWidth / 2 > totalWidth - 1)
            {
                empty.X = totalWidth - EdgeCheckWidth;
            }
            else
            {
                empty.X = MousePoint.X - EdgeCheckWidth / 2;
            }
            if (MousePoint.Y - EdgeCheckHeight / 2 < 0)
            {
                empty.Y = 0;
            }
            else if (MousePoint.Y + EdgeCheckHeight / 2 > totalHeight - 1)
            {
                empty.Y = totalHeight - EdgeCheckHeight;
            }
            else
            {
                empty.Y = MousePoint.Y - EdgeCheckHeight / 2;
            }
            var e = GetBitmapSobelEdge(new DRectangle(empty.X, empty.Y, EdgeCheckWidth, EdgeCheckHeight));
            int[] array = new int[e.Width];
            int[] array2 = new int[e.Height];
            for (int i = 0; i < e.Data.Length; i += 3)
            {
                array[i % (e.Width * 3) / 3] += e.Data[i];
                array2[i / (e.Width * 3)] += e.Data[i];
            }
            for (int j = 0; j < array.Length; j++)
            {
                array[j] /= EdgeCheckHeight;
            }
            for (int k = 0; k < array2.Length; k++)
            {
                array2[k] /= EdgeCheckWidth;
            }
            for (int l = 0; l < EdgeCheckOffset; l++)
            {
                if (array[EdgeCheckWidth / 2 + l] > EdgeCheckThreshold)
                {
                    X = empty.X + EdgeCheckWidth / 2 + l;
                    break;
                }
                if (array[EdgeCheckWidth / 2 - l] > EdgeCheckThreshold)
                {
                    X = empty.X + EdgeCheckWidth / 2 - l;
                    break;
                }
            }
            for (int m = 0; m < EdgeCheckOffset; m++)
            {
                if (array2[EdgeCheckHeight / 2 + m] > EdgeCheckThreshold)
                {
                    Y = empty.Y + EdgeCheckHeight / 2 + m;
                    break;
                }
                if (array2[EdgeCheckHeight / 2 - m] > EdgeCheckThreshold)
                {
                    Y = empty.Y + EdgeCheckHeight / 2 - m;
                    break;
                }
            }
            if (MousePoint.X - EdgeCheckWidth / 2 < 0)
            {
                X = -1;
            }
            else if (MousePoint.X + EdgeCheckWidth / 2 > totalWidth - 1)
            {
                X = -1;
            }
            if (MousePoint.Y - EdgeCheckHeight / 2 < 0)
            {
                Y = -1;
            }
            else if (MousePoint.Y + EdgeCheckHeight / 2 > totalHeight - 1)
            {
                Y = -1;
            }
            if (horizontal && vertical)
            {
                PerceptionImagePoint = new DPoint(X, Y);
            }
            else if (horizontal)
            {
                PerceptionImagePoint = new DPoint(-1, Y);
            }
            else if (vertical)
            {
                PerceptionImagePoint = new DPoint(X, -1);
            }
            else
            {
                PerceptionImagePoint = InvalidPerceptionPoint;
            }
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
                    exist = new RegionSelectScreenView(this, MainGrid, x);
                }
                else
                {
                    exist.UpdateView();
                }
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
            _screenBitmapForSobelEdge = _screenBitmap.Clone() as Bitmap;
            _screenBitmapSource = _screenBitmap.ToBitmapSource();
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateScreenBitmap(_screenBitmapSource);
            }
        }

        private void UpdateView()
        {
            _perceptionPoint = InvalidPerceptionPoint;
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateMousePosition(_mousePositionForScreen);
                var point = view.UpdatePerceptionPosition(_mousePositionForScreen.X, _mousePositionForScreen.Y);
                if (point.X == -1 && point.Y == -1) continue;
                _perceptionPoint = point;
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

        private BitmapSobelEdge GetBitmapSobelEdge(DRectangle rect)
        {
            if (_screenBitmapForSobelEdge == null) return null;
            var e = BitmapSobelEdge.FromBitmap(_screenBitmapForSobelEdge.Clone(rect, PixelFormat.Format24bppRgb));
            e.ProcessToGrey();
            e.ProcessSobelEdgeFilter();
            e.ProcessThresholdFilter(100);
            return e;
        }

        private void PushShiftKeyPress()
        {
            if (_shiftKey) return;
            _shiftKey = true;
            _shiftKeyTimer.Start();
        }

        private void PopShiftKeyPress()
        {
            if (!_shiftKey) return;
            _shiftKeyTimer.Stop();
            if (!_shiftKeyHolding)
            {
                NextPerceptionMode();
            }
            _shiftKey = false;
            _shiftKeyHolding = false;
        }

        private void NextPerceptionMode()
        {
            _perceptionMode = (PerceptionMode)(((int)_perceptionMode + 1) % 4);
            UpdateView();
        }

        private DPoint GetPointWithPerception(DPoint point)
        {
            return new DPoint(_perceptionPoint.X == -1 ? point.X : _perceptionPoint.X, _perceptionPoint.Y == -1 ? point.Y : _perceptionPoint.Y);
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
                point = GetPointWithPerception(point);
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
            var position = e.GetPosition(this);
            var point = new DPoint((int)position.X, (int)position.Y);
            _mousePositionForScreen = point;
            if (_closing) return;
            UpdateView();
            if (_selecting)
            {
                _selectingEndPoint = _mousePositionForScreen;
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
                    var point = new DPoint((int)position.X, (int)position.Y);
                    point = GetPointWithPerception(point);
                    _selectingEndPoint = point;
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
                    PushShiftKeyPress();
                    break;
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if (_closing) return;
            switch (e.Key)
            {
                case Key.LeftShift:
                case Key.RightShift:
                    PopShiftKeyPress();
                    break;
            }
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
