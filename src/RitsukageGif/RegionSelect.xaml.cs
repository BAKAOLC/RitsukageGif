using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;
using FScreen = System.Windows.Forms.Screen;

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
            get => _shiftKeyHolding ? PerceptionMode.None : _perceptionMode;
            set => _perceptionMode = value;
        }

        private bool _selected;
        private DPoint _selectedStartPoint;
        private DPoint _selectedEndPoint;

        private DRectangle SelectedRange => _selected
            ? new DRectangle()
            {
                X = Math.Min(_selectedStartPoint.X, _selectedEndPoint.X),
                Y = Math.Min(_selectedStartPoint.Y, _selectedEndPoint.Y),
                Width = Math.Abs(_selectedStartPoint.X - _selectedEndPoint.X),
                Height = Math.Abs(_selectedStartPoint.Y - _selectedEndPoint.Y)
            }
            : default;

        private bool _selecting;
        private bool _selectingMoved;
        private DPoint _selectingStartPoint;
        private DPoint _selectingEndPoint;

        private DRectangle SelectingRange => _selecting
            ? new DRectangle()
            {
                X = Math.Min(_selectingStartPoint.X, _selectingEndPoint.X),
                Y = Math.Min(_selectingStartPoint.Y, _selectingEndPoint.Y),
                Width = Math.Abs(_selectingStartPoint.X - _selectingEndPoint.X),
                Height = Math.Abs(_selectingStartPoint.Y - _selectingEndPoint.Y)
            }
            : default;

        private ScreenInfo[] _allScreens;
        private RegionSelectScreenView[] _allScreenViewGrids;
        private Bitmap _screenBitmap;
        private Bitmap _screenBitmapForSobelEdge;
        private BitmapSource _screenBitmapSource;
        private DPoint _mousePositionForScreen;
        private DPoint _perceptionPoint;

        private WinWindow[] _windows;

        private bool _leftMouse;

        private bool _shiftKey;
        private bool _shiftKeyHolding;
        private readonly Timer _shiftKeyHoldingTimer = new Timer(150) { AutoReset = false };

        private readonly byte SobelThresholdFilter = Settings.Default.SobelThresholdFilter;
        private readonly int EdgeCheckWidth = Settings.Default.EdgeCheckWidth;
        private readonly int EdgeCheckHeight = Settings.Default.EdgeCheckHeight;
        private readonly int EdgeCheckOffset = Settings.Default.EdgeCheckOffset;
        private readonly int EdgeCheckThreshold = Settings.Default.EdgeCheckThreshold;

        private RegionSelect()
        {
            InitializeComponent();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            _shiftKeyHoldingTimer.Elapsed += (s, e) =>
            {
                _shiftKeyHolding = true;
                Dispatcher.Invoke(UpdateMousePosition);
                Dispatcher.Invoke(UpdateSelectedRegion);
            };
        }

        public Task<(bool, SelectedRegionResult)> WaitForResultAsync()
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

        public void ProcessPerceptionProgramArea(DPoint point)
        {
            var windows = _windows.FirstOrDefault(x => x.IsAlive && x.Bounds.Contains(point));
            PerceptionProgramArea = windows?.Bounds ?? default;
            Activate();
        }

        public void ProcessPerceptionNearPoint(DPoint MousePoint, bool horizontal, bool vertical)
        {
            if (_screenBitmapForSobelEdge == null || (!(horizontal || vertical)))
            {
                PerceptionImagePoint = InvalidPerceptionPoint;
                return;
            }

            var resultX = -1;
            var resultY = -1;
            var totalWidth = _screenBitmap.Width;
            var totalHeight = _screenBitmap.Height;
            var halfCheckWidth = EdgeCheckWidth / 2;
            var halfCheckHeight = EdgeCheckHeight / 2;
            var beginX = 0;
            if (MousePoint.X - halfCheckWidth < 0)
            {
                vertical = false;
            }
            else if (MousePoint.X + halfCheckWidth > totalWidth - 1)
            {
                vertical = false;
                beginX = totalWidth - EdgeCheckWidth;
            }
            else
            {
                beginX = MousePoint.X - halfCheckWidth;
            }
            var beginY = 0;
            if (MousePoint.Y - halfCheckHeight < 0)
            {
                horizontal = false;
            }
            else if (MousePoint.Y + halfCheckHeight > totalHeight - 1)
            {
                horizontal = false;
                beginY = totalHeight - EdgeCheckHeight;
            }
            else
            {
                beginY = MousePoint.Y - halfCheckHeight;
            }
            var bitX = new int[EdgeCheckWidth];
            var bitY = new int[EdgeCheckHeight];
            var e = GetBitmapSobelEdge(new DRectangle(beginX, beginY, EdgeCheckWidth, EdgeCheckHeight));
            for (var y = 0; y < e.Height; y++)
            {
                for (var x = 0; x < e.Width; x++)
                {
                    var index = y * e.Width + x;
                    var color = e.Data[index * 3];
                    bitX[x] += color;
                    bitY[y] += color;
                }
            }
            
            if (vertical)
            {
                for (var x = 0; x < bitX.Length; x++)
                {
                    bitX[x] /= EdgeCheckHeight;
                }

                for (var x = 0; x < halfCheckWidth; x++)
                {
                    if (bitX[halfCheckWidth + x] > EdgeCheckThreshold)
                    {
                        resultX = beginX + halfCheckWidth + x;
                        break;
                    }

                    if (bitX[halfCheckWidth - x] > EdgeCheckThreshold)
                    {
                        resultX = beginX + halfCheckWidth - x;
                        break;
                    }
                }
            }
            
            if (horizontal)
            {
                for (var y = 0; y < bitY.Length; y++)
                {
                    bitY[y] /= EdgeCheckWidth;
                }

                for (var y = 0; y < halfCheckHeight; y++)
                {
                    if (bitY[halfCheckHeight + y] > EdgeCheckThreshold)
                    {
                        resultY = beginY + halfCheckHeight + y;
                        break;
                    }

                    if (bitY[halfCheckHeight - y] > EdgeCheckThreshold)
                    {
                        resultY = beginY + halfCheckHeight - y;
                        break;
                    }
                }
            }

            PerceptionImagePoint = horizontal && vertical
                ? new DPoint(resultX, resultY)
                : horizontal
                    ? new DPoint(-1, resultY)
                    : vertical
                        ? new DPoint(resultX, -1)
                        : InvalidPerceptionPoint;
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
            UpdateWindowEnums();
            var point = Mouse.GetPosition(this);
            _mousePositionForScreen = new DPoint((int)point.X, (int)point.Y);
            UpdateMousePosition();
            UpdateSelectedRegion();
        }

        private void UpdateWindowEnums()
        {
            var currentHandle = new WindowInteropHelper(this).Handle;
            _windows = WinWindow.Enumerate()
                .Where(x => x.Handle != currentHandle && x.IsVisible)
                .SelectMany(GetAllChildren)
                .ToArray();
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

        private void UpdateMousePosition()
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

        private void UpdateSelectedRegion()
        {
            DRectangle rect;
            bool needConvert = false;
            double opacity = 1;
            if (_selecting)
            {
                rect = SelectingRange;
                needConvert = true;
            }
            else if (_selected)
            {
                rect = SelectedRange;
                needConvert = true;
            }
            else
            {
                rect = PerceptionProgramArea;
                opacity = 0.8;
            }

            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateSelectedRegion(rect, needConvert, opacity);
            }
        }

        private BitmapSobelEdge GetBitmapSobelEdge(DRectangle rect)
        {
            if (_screenBitmapForSobelEdge == null) return null;
            var e = BitmapSobelEdge.FromBitmap(_screenBitmapForSobelEdge.Clone(rect, PixelFormat.Format24bppRgb));
            e.ProcessToGrey();
            e.ProcessSobelEdgeFilter();
            e.ProcessThresholdFilter(SobelThresholdFilter);
            return e;
        }

        private void PushShiftKeyPress()
        {
            if (_shiftKey) return;
            _shiftKey = true;
            _shiftKeyHolding = false;
            _shiftKeyHoldingTimer.Start();
        }

        private void PopShiftKeyPress()
        {
            if (!_shiftKey) return;
            _shiftKeyHoldingTimer.Stop();
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
            Dispatcher.Invoke(UpdateMousePosition);
            Dispatcher.Invoke(UpdateSelectedRegion);
        }

        private DPoint GetPointWithPerception(DPoint point)
        {
            return new DPoint(_perceptionPoint.X == -1 ? point.X : _perceptionPoint.X,
                _perceptionPoint.Y == -1 ? point.Y : _perceptionPoint.Y);
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
            if (e.LeftButton == MouseButtonState.Pressed && !_leftMouse)
            {
                _leftMouse = true;
                _selecting = true;
                _selectingMoved = false;
                var position = e.GetPosition(this);
                var point = new DPoint((int)position.X, (int)position.Y);
                point = GetPointWithPerception(point);
                _selectingStartPoint = point;
                _selectingEndPoint = point;
            }

            if (e.RightButton == MouseButtonState.Pressed)
            {
                if (_selecting)
                {
                    _selecting = false;
                    _selected = true;
                    if (_selectingMoved)
                    {
                        var position = e.GetPosition(this);
                        var point = new DPoint((int)position.X, (int)position.Y);
                        point = GetPointWithPerception(point);
                        _selectingEndPoint = point;
                        _selectedStartPoint = _selectingStartPoint;
                        _selectedEndPoint = _selectingEndPoint;
                    }
                    else
                    {
                        var view = ScreenInfo.MainScreen;
                        if (view == null) return;
                        _selectedStartPoint =
                            view.ConvertFromScalePoint(
                                new DPoint(PerceptionProgramArea.Left, PerceptionProgramArea.Top));
                        _selectedEndPoint = view.ConvertFromScalePoint(new DPoint(PerceptionProgramArea.Right,
                            PerceptionProgramArea.Bottom));
                    }
                }

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
            if (_selecting)
            {
                _selectingMoved = true;
                _selectingEndPoint = _mousePositionForScreen;
            }

            UpdateMousePosition();
            UpdateSelectedRegion();
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_closing) return;
            if (_leftMouse && e.LeftButton == MouseButtonState.Released)
            {
                _leftMouse = false;
                if (_selecting)
                {
                    _selecting = false;
                    _selected = true;
                    if (_selectingMoved)
                    {
                        var position = e.GetPosition(this);
                        var point = new DPoint((int)position.X, (int)position.Y);
                        point = GetPointWithPerception(point);
                        _selectingEndPoint = point;
                        _selectedStartPoint = _selectingStartPoint;
                        _selectedEndPoint = _selectingEndPoint;
                        UpdateSelectedRegion();
                    }
                    else
                    {
                        var view = ScreenInfo.MainScreen;
                        if (view == null) return;
                        _selectedStartPoint =
                            view.ConvertFromScalePoint(
                                new DPoint(PerceptionProgramArea.Left, PerceptionProgramArea.Top));
                        _selectedEndPoint = view.ConvertFromScalePoint(new DPoint(PerceptionProgramArea.Right,
                            PerceptionProgramArea.Bottom));
                        UpdateSelectedRegion();
                    }
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
                        UpdateSelectedRegion();
                    }
                    else if (_selected)
                    {
                        _selected = false;
                        UpdateSelectedRegion();
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

        private static IEnumerable<WinWindow> GetAllChildren(WinWindow window)
        {
            var children = window
                .EnumerateChildren()
                .Where(w => w.IsVisible);
            foreach (var child in children)
            {
                foreach (var grandchild in GetAllChildren(child))
                {
                    yield return grandchild;
                }
            }

            yield return window;
        }
    }
}