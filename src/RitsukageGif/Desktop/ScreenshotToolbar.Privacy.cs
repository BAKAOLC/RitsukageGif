using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    internal sealed partial class ScreenshotToolbar
    {
        private readonly StackPanel _privacyCommands = new()
            { Orientation = Orientation.Horizontal, Spacing = 8 };

        private readonly TextBlock _privacyStatus = new() { VerticalAlignment = VerticalAlignment.Center };
        private Button? _privacyApply;
        private Button? _privacyDetect;

        private void InitializePrivacyCommands()
        {
            var detect = _privacyDetect = MenuButton("AnnotationAutoMosaic", () =>
                {
                    if (!_session.RequestPrivacyDetection()) return;
                    HideToolGroups();
                    _contextFrame.IsVisible = false;
                },
                "M3,3 H9 V9 H3 Z M15,3 H21 V9 H15 Z M3,15 H9 V21 H3 Z M15,15 H21 V21 H15 Z");
            _recognitionButtons.Add(detect);
            _privacyCommands.Children.Add(_privacyStatus);
            _privacyApply = MenuButton("AnnotationPrivacyApply", () => _ = _session.ApplyPrivacyMosaicAsync(),
                ToolbarIcon.Geometry("ScreenshotApplyText"));
            _privacyCommands.Children.Add(_privacyApply);
            _privacyCommands.Children.Add(MenuButton("Cancel", _session.CancelPrivacyDetection,
                "M5,5 L19,19 M19,5 L5,19"));
            _commandRow.Children.Add(_privacyCommands);
        }

        private void RefreshPrivacyCommands()
        {
            var active = _session.IsPrivacyDetectionActive;
            foreach (var control in _commandRow.Children)
                control.IsVisible = ReferenceEquals(control, _privacyCommands)
                    ? active
                    : !active && (!ReferenceEquals(control, _moveHandle) || !IsDocked);
            if (!active) return;
            _privacyStatus.Text = _session.PrivacyDetectionPending
                ? UiText.Get("AnnotationPrivacyDetecting")
                : string.Format(CultureInfo.CurrentCulture, UiText.Get("AnnotationPrivacyCandidates"),
                    _session.SelectedPrivacyCandidateCount, _session.PrivacyCandidates.Count);
            _privacyApply?.IsEnabled = _session.CanApplyPrivacyMosaic;
        }
    }
}
