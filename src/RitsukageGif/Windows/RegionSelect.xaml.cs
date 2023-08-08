using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using RitsukageGif.Class;
using RitsukageGif.Enums;
using RitsukageGif.Extensions;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;
using FScreen = System.Windows.Forms.Screen;

namespace RitsukageGif.Windows
{
    /// <summary>
    ///     RegionSelect.xaml 的交互逻辑
    /// </summary>
    public partial class RegionSelect : Window
    {
        public static readonly DPoint InvalidPoint = new DPoint(int.MinValue, int.MinValue);
        private readonly int _edgeCheckHeight = Settings.Default.EdgeCheckHeight;
        private readonly int _edgeCheckOffset = Settings.Default.EdgeCheckOffset;
        private readonly int _edgeCheckThreshold = Settings.Default.EdgeCheckThreshold;
        private readonly int _edgeCheckWidth = Settings.Default.EdgeCheckWidth;
        private readonly Timer _shiftKeyHoldingTimer = new Timer(150) { AutoReset = false };

        private readonly byte _sobelThresholdFilter = Settings.Default.SobelThresholdFilter;

        private ScreenInfo[] _allScreens;
        private RegionSelectScreenView[] _allScreenViewGrids;

        private bool _closing;
        private bool _confirm;

        private bool _leftMouse;
        private DPoint _mousePositionForScreen;
        private bool _ok;

        private PerceptionMode _perceptionMode = PerceptionMode.Both;
        private DPoint _perceptionPoint;
        private Bitmap _screenBitmap;
        private Bitmap _screenBitmapForSobelEdge;
        private BitmapSource _screenBitmapSource;

        private bool _selected;
        private DPoint _selectedEndPoint;
        private DPoint _selectedStartPoint;

        private bool _selecting;
        private DPoint _selectingEndPoint;
        private bool _selectingMoved;
        private DPoint _selectingStartPoint;

        private bool _shiftKey;
        private bool _shiftKeyHolding;

        private WinWindow[] _windows;

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

        public DPoint PerceptionImagePoint { get; private set; }

        public DRectangle PerceptionProgramArea { get; private set; }

        public PerceptionMode PerceptionMode
        {
            get => _shiftKeyHolding ? PerceptionMode.None : _perceptionMode;
            set => _perceptionMode = value;
        }

        private DRectangle SelectedRange => _selected
            ? new DRectangle
            {
                X = Math.Min(_selectedStartPoint.X, _selectedEndPoint.X),
                Y = Math.Min(_selectedStartPoint.Y, _selectedEndPoint.Y),
                Width = Math.Abs(_selectedStartPoint.X - _selectedEndPoint.X),
                Height = Math.Abs(_selectedStartPoint.Y - _selectedEndPoint.Y)
            }
            : default;

        private DRectangle SelectingRange => _selecting
            ? new DRectangle
            {
                X = Math.Min(_selectingStartPoint.X, _selectingEndPoint.X),
                Y = Math.Min(_selectingStartPoint.Y, _selectingEndPoint.Y),
                Width = Math.Abs(_selectingStartPoint.X - _selectingEndPoint.X),
                Height = Math.Abs(_selectingStartPoint.Y - _selectingEndPoint.Y)
            }
            : default;

        public DRectangle ScreenRectangle { get; private set; }

        public Task<(bool, SelectedRegionResult)> WaitForResultAsync()
        {
            var tcs = new TaskCompletionSource<(bool, SelectedRegionResult)>();
            var timer = new Timer(100);
            timer.Elapsed += (s, e) =>
            {
                if (!_closing || !_ok) return;
                timer.Stop();
                var rect = SelectedRange;
                var regions = _allScreens.Select(x => new ScreenRegion(x, x.GetConvertedIntersectionRegion(rect)))
                    .Where(x => x.Rectangle != default)
                    .ToArray();
                var result = new SelectedRegionResult(rect, regions);
                if (_confirm && rect != default)
                    tcs.SetResult((_confirm, result));
                else
                    tcs.SetResult((_confirm, null));
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

        public void ProcessPerceptionNearPoint(DPoint mousePoint, bool horizontal, bool vertical)
        {
            if (_screenBitmapForSobelEdge == null || !(horizontal || vertical))
            {
                PerceptionImagePoint = InvalidPoint;
                return;
            }

            var mousePointX = mousePoint.X;
            var mousePointY = mousePoint.Y;
            var screenBeginPosX = ScreenRectangle.Left;
            var screenBeginPosY = ScreenRectangle.Top;
            var screenEndPosX = ScreenRectangle.Right;
            var screenEndPosY = ScreenRectangle.Bottom;
            var resultX = InvalidPoint.X;
            var resultY = InvalidPoint.Y;
            var halfCheckWidth = _edgeCheckWidth / 2;
            var halfCheckHeight = _edgeCheckHeight / 2;
            var beginX = 0;
            var beginY = 0;
            if (mousePointX - halfCheckWidth < screenBeginPosX)
            {
                vertical = false;
            }
            else if (mousePointX + halfCheckWidth > screenEndPosX - 1)
            {
                vertical = false;
                beginX = screenEndPosX - _edgeCheckWidth;
            }
            else
            {
                beginX = mousePointX - halfCheckWidth;
            }

            if (mousePointY - halfCheckHeight < screenBeginPosY)
            {
                horizontal = false;
            }
            else if (mousePointY + halfCheckHeight > screenEndPosY - 1)
            {
                horizontal = false;
                beginY = screenEndPosY - _edgeCheckHeight;
            }
            else
            {
                beginY = mousePointY - halfCheckHeight;
            }

            var bitX = new int[_edgeCheckWidth];
            var bitY = new int[_edgeCheckHeight];
            var e = GetBitmapSobelEdge(new DRectangle(beginX - screenBeginPosX, beginY - screenBeginPosY,
                _edgeCheckWidth,
                _edgeCheckHeight));

            for (var y = 0; y < e.Height; y++)
            for (var x = 0; x < e.Width; x++)
            {
                var index = y * e.Width + x;
                var color = e.Data[index * 3];
                bitX[x] += color;
                bitY[y] += color;
            }

            if (vertical)
            {
                for (var x = 0; x < bitX.Length; x++) bitX[x] /= _edgeCheckHeight;

                for (var x = 0; x < _edgeCheckOffset; x++)
                {
                    if (bitX[halfCheckWidth + x] > _edgeCheckThreshold)
                    {
                        resultX = beginX + halfCheckWidth + x;
                        break;
                    }

                    if (bitX[halfCheckWidth - x] > _edgeCheckThreshold)
                    {
                        resultX = beginX + halfCheckWidth - x;
                        break;
                    }
                }
            }

            if (horizontal)
            {
                for (var y = 0; y < bitY.Length; y++) bitY[y] /= _edgeCheckWidth;

                for (var y = 0; y < _edgeCheckOffset; y++)
                {
                    if (bitY[halfCheckHeight + y] > _edgeCheckThreshold)
                    {
                        resultY = beginY + halfCheckHeight + y;
                        break;
                    }

                    if (bitY[halfCheckHeight - y] > _edgeCheckThreshold)
                    {
                        resultY = beginY + halfCheckHeight - y;
                        break;
                    }
                }
            }

            PerceptionImagePoint = horizontal && vertical
                ? new DPoint(resultX, resultY)
                : horizontal
                    ? new DPoint(InvalidPoint.X, resultY)
                    : vertical
                        ? new DPoint(resultX, InvalidPoint.Y)
                        : InvalidPoint;
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
            _mousePositionForScreen = new DPoint((int)point.X + ScreenRectangle.X, (int)point.Y + ScreenRectangle.Y);
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
                    exist = ScreenInfo.GetScreenInfo(x);
                else
                    exist.UpdateDpiScale();

                return exist;
            })?.ToArray();
        }

        private void UpdateScreenViews()
        {
            _allScreenViewGrids = _allScreens.Select(x =>
            {
                var exist = _allScreenViewGrids?.FirstOrDefault(y => y.Screen == x);
                if (exist == null)
                    exist = new RegionSelectScreenView(this, MainGrid, x);
                else
                    exist.UpdateView();

                return exist;
            })?.ToArray();
        }

        private DRectangle GetScreenFullSize()
        {
            UpdateScreens();
            var minX = _allScreens.Min(x => x.Bounds.Left);
            var minY = _allScreens.Min(x => x.Bounds.Top);
            var maxX = _allScreens.Max(x => x.Bounds.Right);
            var maxY = _allScreens.Max(x => x.Bounds.Bottom);
            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        private void UpdateSizeToScreenSize()
        {
            ScreenRectangle = GetScreenFullSize();
            Left = ScreenRectangle.Left;
            Top = ScreenRectangle.Top;
            MainGrid.Width = Width = ScreenRectangle.Width;
            MainGrid.Height = Height = ScreenRectangle.Height;
            UpdateScreenViews();
        }

        private void UpdateScreenBitmap()
        {
            _screenBitmap = new Bitmap(ScreenRectangle.Width, ScreenRectangle.Height);
            using (var graphics = Graphics.FromImage(_screenBitmap))
            {
                foreach (var screen in _allScreens)
                {
                    var drawPos = new DPoint(screen.Bounds.Left - ScreenRectangle.Left,
                        screen.Bounds.Top - ScreenRectangle.Top);
                    graphics.CopyFromScreen(screen.Bounds.Location, drawPos, screen.Bounds.Size);
                }
            }

            _screenBitmapForSobelEdge = _screenBitmap.Clone() as Bitmap;
            _screenBitmapSource = _screenBitmap.ToBitmapSource();
            foreach (var view in _allScreenViewGrids) view.UpdateScreenBitmap(_screenBitmapSource);
        }

        private void UpdateMousePosition()
        {
            _perceptionPoint = InvalidPoint;
            foreach (var view in _allScreenViewGrids)
            {
                view.UpdateMousePosition(_mousePositionForScreen);
                var point = view.UpdatePerceptionPosition(_mousePositionForScreen.X, _mousePositionForScreen.Y);
                if (point.X == InvalidPoint.X && point.Y == InvalidPoint.Y) continue;
                _perceptionPoint = point;
            }
        }

        private void UpdateSelectedRegion()
        {
            DRectangle rect;
            var needConvert = false;
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

            foreach (var view in _allScreenViewGrids) view.UpdateSelectedRegion(rect, needConvert, opacity);
        }

        private BitmapSobelEdge GetBitmapSobelEdge(DRectangle rect)
        {
            if (_screenBitmapForSobelEdge == null) return null;
            var e = BitmapSobelEdge.FromBitmap(_screenBitmapForSobelEdge.Clone(rect, PixelFormat.Format24bppRgb));
            e.ProcessToGrey();
            e.ProcessSobelEdgeFilter();
            e.ProcessThresholdFilter(_sobelThresholdFilter);
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
            if (!_shiftKeyHolding) NextPerceptionMode();

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
            return new DPoint(_perceptionPoint.X == InvalidPoint.X ? point.X : _perceptionPoint.X,
                _perceptionPoint.Y == InvalidPoint.Y ? point.Y : _perceptionPoint.Y);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
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
                var point = new DPoint((int)position.X + ScreenRectangle.X, (int)position.Y + ScreenRectangle.Y);
                point = GetPointWithPerception(point);
                _selectingStartPoint = point;
                _selectingEndPoint = point;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(this);
            var point = new DPoint((int)position.X + ScreenRectangle.X, (int)position.Y + ScreenRectangle.Y);
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
                        var point = new DPoint((int)position.X + ScreenRectangle.X,
                            (int)position.Y + ScreenRectangle.Y);
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
                return;
            }
            if (e.RightButton == MouseButtonState.Released)
            {
                if (_selecting)
                {
                    _selecting = false;
                    _selected = true;
                    e.Handled = true;
                    if (_selectingMoved)
                    {
                        var position = e.GetPosition(this);
                        var point = new DPoint((int)position.X + ScreenRectangle.X,
                            (int)position.Y + ScreenRectangle.Y);
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
            foreach (var grandchild in GetAllChildren(child))
                yield return grandchild;

            yield return window;
        }
    }
}