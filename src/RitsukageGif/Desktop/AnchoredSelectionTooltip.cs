using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace RitsukageGif.Desktop
{
    internal static class AnchoredSelectionTooltip
    {
        internal static void Configure(Control control, bool enabled = true)
        {
            ToolTip.SetCustomPopupPlacementCallback(control, enabled ? Place : null);
            ToolTip.SetPlacement(control, enabled ? PlacementMode.Custom : PlacementMode.Pointer);
            if (!enabled) return;
            ToolTip.SetShowDelay(control, 1000);
            ToolTip.SetBetweenShowDelay(control, -1);
        }

        private static void Place(CustomPopupPlacement placement)
        {
            const double belowGap = 4;
            const double clearance = 30;
            const double aboveOffset = 24;
            var target = placement.Target;
            var origin = target.PointToScreen(default);
            var below = target.PointToScreen(new(0, target.Bounds.Height + belowGap));
            var threshold = target.PointToScreen(new(0, target.Bounds.Height + belowGap + clearance));
            var screen = TopLevel.GetTopLevel(target)?.Screens?.ScreenFromPoint(origin);
            var above = screen is not null &&
                        (threshold.Y >= screen.WorkingArea.Bottom ||
                         below.Y + placement.PopupSize.Height * screen.Scaling > screen.WorkingArea.Bottom);
            placement.Anchor = above ? PopupAnchor.Top : PopupAnchor.Bottom;
            placement.Gravity = above ? PopupGravity.Top : PopupGravity.Bottom;
            placement.Offset = new(0, above ? -aboveOffset : belowGap);
            placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX |
                                             PopupPositionerConstraintAdjustment.SlideY;
        }
    }
}
