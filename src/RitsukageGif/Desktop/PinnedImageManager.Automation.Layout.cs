namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedImageManager
    {
        private sealed partial class AutomationService
        {
            public Task<IReadOnlyList<PinInfo>> ArrangeAsync(IReadOnlyList<string> ids, PinArrangement arrangement,
                bool allowNegativeSpacing = false, CancellationToken token = default)
            {
                return OnUi(() =>
                {
                    if (!Enum.IsDefined(arrangement) || ids.Count < 2 ||
                        ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                        throw new ArgumentException("At least two distinct pins and a valid arrangement are required.");
                    var pins = ids.Select(manager.AutomationFind).ToArray();
                    if (pins.Any(pin => !pin.CanArrangeSelection))
                        throw new InvalidOperationException("One or more pins cannot be arranged.");
                    var screen = ArrangementScreen(pins[0], pins[0].Window.Screens) ??
                                 throw new InvalidOperationException("The pin has no available screen.");
                    if (pins.Any(pin => ArrangementScreen(pin, pins[0].Window.Screens) != screen))
                        throw new InvalidOperationException("The selected pins must be on the same screen.");
                    var operation = arrangement switch
                    {
                        PinArrangement.AlignLeft => PinnedImageArrangement.AlignLeft,
                        PinArrangement.AlignCenter => PinnedImageArrangement.AlignCenter,
                        PinArrangement.AlignRight => PinnedImageArrangement.AlignRight,
                        PinArrangement.AlignTop => PinnedImageArrangement.AlignTop,
                        PinArrangement.AlignMiddle => PinnedImageArrangement.AlignMiddle,
                        PinArrangement.AlignBottom => PinnedImageArrangement.AlignBottom,
                        PinArrangement.DistributeHorizontal => PinnedImageArrangement.DistributeHorizontal,
                        PinArrangement.DistributeVertical => PinnedImageArrangement.DistributeVertical,
                        PinArrangement.CompactHorizontal => PinnedImageArrangement.CompactHorizontal,
                        PinArrangement.CompactVertical => PinnedImageArrangement.CompactVertical,
                        PinArrangement.Compact => PinnedImageArrangement.Compact,
                        _ => throw new ArgumentOutOfRangeException(nameof(arrangement)),
                    };
                    var bounds = pins.Select(pin => pin.SelectionLayoutBounds).ToArray();
                    var original = pins.Select(pin => pin.Window.Position).ToArray();
                    PixelPoint[] positions;
                    switch (operation)
                    {
                        case PinnedImageArrangement.DistributeHorizontal:
                        case PinnedImageArrangement.DistributeVertical:
                            if (pins.Length < 3)
                                throw new ArgumentException("Distribution requires at least three pins.");
                            var horizontal = operation == PinnedImageArrangement.DistributeHorizontal;
                            if (!allowNegativeSpacing &&
                                PinnedReferenceSelectionLayout.DistributionSpacing(bounds, horizontal) < 0)
                                throw new InvalidOperationException(
                                    "This distribution requires allowNegativeSpacing: true.");
                            positions = PinnedReferenceSelectionLayout.Distribute(bounds, horizontal);
                            break;
                        case PinnedImageArrangement.Compact:
                            var area = screen.WorkingArea;
                            if (!PinnedCompactLayout.TryArrangeCompact(bounds,
                                    new(area.X, area.Y, area.Width, area.Height), out positions))
                                throw new InvalidOperationException("The pins do not fit in the screen working area.");
                            break;
                        case PinnedImageArrangement.AlignTop:
                        case PinnedImageArrangement.AlignMiddle:
                        case PinnedImageArrangement.AlignBottom:
                        case PinnedImageArrangement.AlignLeft:
                        case PinnedImageArrangement.AlignRight:
                            positions = PinnedReferenceSelectionLayout.Align(bounds, 0, operation);
                            break;
                        case PinnedImageArrangement.AlignCenter:
                        case PinnedImageArrangement.CompactHorizontal:
                        case PinnedImageArrangement.CompactVertical:
                            positions = PinnedImageLayoutEngine.Arrange(bounds, operation, screen.WorkingArea.Width);
                            break;
                        default: throw new ArgumentOutOfRangeException(nameof(arrangement));
                    }

                    try
                    {
                        for (var index = 0; index < pins.Length; index++)
                            pins[index].Window.Position = new(original[index].X + positions[index].X - bounds[index].X,
                                original[index].Y + positions[index].Y - bounds[index].Y);
                    }
                    catch (Exception failure)
                    {
                        var errors = new List<Exception> { failure };
                        for (var index = 0; index < pins.Length; index++)
                            try
                            {
                                pins[index].Window.Position = original[index];
                            }
                            catch (Exception error)
                            {
                                errors.Add(error);
                            }

                        if (errors.Count > 1) throw new AggregateException(errors);
                        throw;
                    }

                    manager.ScheduleSave();
                    PinInfo[] arranged = [.. pins.Select(AutomationInfo)];
                    return Task.FromResult<IReadOnlyList<PinInfo>>(arranged);
                }, token);
            }
        }
    }
}
