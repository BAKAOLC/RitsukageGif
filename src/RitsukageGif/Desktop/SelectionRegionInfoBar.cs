using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace RitsukageGif.Desktop
{
    internal sealed partial class SelectionRegionInfoBar : Border, IDisposable
    {
        private readonly Button _add;

        private readonly TextBlock _coordinateText = new()
            { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };

        private readonly Button _coordinates;
        private readonly SelectionCoordinator _coordinator;
        private readonly SelectionGeometryPanel _geometry;
        private readonly Flyout _manual;
        private readonly Button _remove;
        private readonly HoverPropertyPanel _displayOptions;

        private readonly TextBlock _semantic = new()
        {
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(204, 207, 212)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        private readonly TextBlock _title = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        private IDisposable? _dialogScope;

        private (SelectionSnapshot Snapshot, bool Measuring, bool ShowInformation, SelectionHintMode Hints, string
            Culture, string Unit, SelectionSizeUnit DisplayUnit, SelectionInfoItems DisplayItems,
            double Density)? _textKey;

        internal SelectionRegionInfoBar(SelectionCoordinator coordinator)
        {
            _coordinator = coordinator;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Background = new SolidColorBrush(Color.FromArgb(250, 30, 32, 36));
            BorderBrush = new SolidColorBrush(Color.FromRgb(104, 108, 116));
            BorderThickness = new(1);
            CornerRadius = new(5);
            Padding = new(9, 5);
            _title.Foreground = _coordinateText.Foreground = Brushes.White;
            _coordinates = new()
            {
                Height = 22, MinHeight = 0, MinWidth = 0, Padding = new(0), Focusable = false, IsTabStop = false,
                Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new(0),
                Content = _coordinateText, Cursor = new(StandardCursorType.Hand),
            };
            _coordinates.Bind(ToolTip.TipProperty, LiveText.Resource("SelectionGeometry"));
            _coordinates.Bind(AutomationProperties.NameProperty, LiveText.Resource("SelectionGeometry"));
            _displayOptions = new(_coordinates, CreateDisplayOptions, RefreshDisplayOptions);
            _geometry = new(coordinator) { Width = 280 };
            _manual = new() { Content = _geometry, Placement = PlacementMode.BottomEdgeAlignedLeft };
            _coordinates.Click += (_, _) =>
            {
                _displayOptions.Hide();
                if (_dialogScope is not null)
                {
                    _manual.Hide();
                    return;
                }

                _geometry.Update(coordinator.Snapshot);
                _dialogScope = coordinator.BeginSelectionDialog();
                _displayOptions.IsEnabled = false;
                try
                {
                    _manual.ShowAt(_coordinates);
                }
                catch
                {
                    CloseManual();
                    throw;
                }
            };
            _manual.Closed += (_, _) => CloseManual();
            _add = CreateAction("SelectionAddArea", ToolbarIcon.GetDefinition("SelectionAddArea"), false);
            _remove = CreateAction("SelectionRemoveArea", ToolbarIcon.GetDefinition("SelectionRemoveArea"), true);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { _coordinates, _add, _remove },
            };
            Child = new StackPanel { Spacing = 0, Children = { _title, _semantic, row } };
            IsVisible = false;
        }

        internal Rect OccupiedBounds { get; private set; }

        public void Dispose()
        {
            _displayOptions.Dispose();
            _manual.Hide();
            CloseManual();
        }

        private void CloseManual()
        {
            _dialogScope?.Dispose();
            _dialogScope = null;
            _displayOptions.IsEnabled = IsVisible;
        }

        internal void Update(PixelRect monitor, double scale, bool owner, Rect toolbar, Rect options, Rect side,
            Rect modePicker)
        {
            var snapshot = _coordinator.Snapshot;
            var information = _coordinator.CapturePreferences.ShowInformation;
            var measuring = _coordinator is { AuxiliaryVisible: true, CapturePreferences.GuideMeasurements: true };
            IsVisible = owner && !_coordinator.ScreenshotPreview && snapshot.Phase == SelectionPhase.Selected &&
                        _coordinator.Annotations is null or { IsBusy: false, IsDrawing: false, IsEditingText: false } &&
                        (information || _coordinator.CanEditShapeRegions);
            if (!IsVisible)
            {
                _displayOptions.IsEnabled = false;
                OccupiedBounds = default;
                _manual.Hide();
                return;
            }

            _displayOptions.IsEnabled = _dialogScope is null && information;
            var preferences = _coordinator.CapturePreferences;
            var density = _coordinator.InformationDensity;
            var textKey = (snapshot, measuring, information, _coordinator.HintMode, CultureInfo.CurrentUICulture.Name,
                _coordinator.SelectionCoordinateUnit, preferences.InformationUnit, preferences.InformationItems,
                density);
            var contentChanged = _textKey != textKey;
            if (contentChanged)
            {
                _textKey = textKey;
                var region = snapshot.Region;
                _coordinateText.Text = FormatInformation(region, preferences.InformationUnit,
                    preferences.InformationItems, density,
                    _coordinator.SelectionCoordinateUnit == "pt", measuring);
                var target = snapshot.Target;
                _title.IsVisible = information && target is not null && !measuring &&
                                   _coordinator.HintMode != SelectionHintMode.Hidden;
                _semantic.IsVisible = information && target is not null &&
                                      _coordinator.HintMode != SelectionHintMode.Hidden;
                if (target is not null)
                {
                    var title = string.IsNullOrWhiteSpace(target.Title) ? target.Process : target.Title;
                    _title.Text = title[..Math.Min(160, title.Length)].Replace('\r', ' ').Replace('\n', ' ');
                    _semantic.Text =
                        UiText.Get(snapshot.WindowSource ? "SelectionCaptureWindow" : "SelectionCaptureScreen");
                    if (snapshot.FollowsWindow && !_coordinator.ScreenshotMode)
                        _semantic.Text = UiText.Get("SelectionCaptureWindowFollowing");
                }
            }

            var canEditShapeRegions = _coordinator.CanEditShapeRegions;
            contentChanged |= _coordinates.IsVisible != information || _add.IsVisible != canEditShapeRegions;
            _coordinates.IsVisible = information;
            _add.IsVisible = _remove.IsVisible = canEditShapeRegions;
            var viewport = new Size(monitor.Width / scale, monitor.Height / scale);
            MaxWidth = Math.Max(1, Math.Min(280, viewport.Width - 16));
            var rowButtons = _add.IsVisible ? 56 : 0;
            _coordinateText.MaxWidth = Math.Max(1, MaxWidth - 20 - rowButtons);
            _title.MaxWidth = _semantic.MaxWidth = Math.Max(1, MaxWidth - 20);
            if (contentChanged)
            {
                for (var ancestor = _coordinateText.GetVisualParent();
                     ancestor is Layoutable layout && !ReferenceEquals(layout, this);
                     ancestor = ancestor.GetVisualParent())
                    layout.InvalidateMeasure();
                Child!.InvalidateMeasure();
            }

            Child!.Measure(new(Math.Max(1, MaxWidth - 20), Math.Max(1, viewport.Height - 12)));
            var size = new Size(Math.Min(MaxWidth, Child.DesiredSize.Width + 20), Child.DesiredSize.Height + 12);
            var local = snapshot.Region.ToLocal(monitor, scale);
            var offsets = _coordinator.RegionInformationOffsets(snapshot.Region, scale);
            OccupiedBounds = SelectionRegionInfoLayout.Place(viewport,
                new(local.X, local.Y, local.Width, local.Height), size, toolbar, options, side, modePicker,
                offsets.Near, offsets.Far);
            Margin = new(OccupiedBounds.X, OccupiedBounds.Y, 0, 0);
            _geometry.Update(snapshot);
        }

        private Button CreateAction(string key, IconDefinition path, bool subtract)
        {
            var button = new Button
            {
                Width = 24, Height = 22, MinWidth = 0, MinHeight = 0, Padding = new(3),
                Focusable = false, IsTabStop = false, Background = Brushes.Transparent,
                Foreground = Brushes.White, BorderThickness = new(0), Cursor = new(StandardCursorType.Hand),
            };
            var icon = ToolbarIcon.Create(button, path);
            icon.Width = icon.Height = 16;
            button.Content = icon;
            button.Bind(ToolTip.TipProperty, LiveText.Resource(key));
            AnchoredSelectionTooltip.Configure(button);
            button.Bind(AutomationProperties.NameProperty, LiveText.Resource(key));
            button.Click += (_, _) => _coordinator.BeginRegionEdit(subtract);
            return button;
        }
    }
}
