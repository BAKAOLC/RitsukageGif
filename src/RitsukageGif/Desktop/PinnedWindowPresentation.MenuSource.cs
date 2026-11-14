using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed partial class PinnedWindowPresentation
    {
        private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly Func<Func<Task>, Task> _runOperation;
        private readonly TextBlock _status;
        private string? _message;
        internal Func<string, (int Visible, int Total)> GroupCountsProvider { get; set; } = _ => (0, 0);
        internal Func<bool> HideOthersCheckedProvider { get; set; } = () => false;
        internal Func<ContextMenu?> SelectionMenuProvider { get; set; } = () => null;

        internal Task RunOperationAsync(Func<Task> operation)
        {
            return _runOperation(operation);
        }

        internal void ShowMessage(string message)
        {
            _messageTimer.Stop();
            _message = message;
            _status.Text = message;
            _status.IsVisible = true;
            _messageTimer.Start();
        }

        internal void ShowMouseThroughMessage(bool enabled)
        {
            ShowMessage(UiText.Get(enabled ? "PinnedMouseThroughEnabled" : "PinnedMouseThroughDisabled"));
        }

        internal void ShowOpacityMessage()
        {
            var percent = (int)Math.Floor(_owner.Opacity * 100 + .5);
            ShowMessage(UiText.Format("PinnedOpacityStatus", percent));
        }

        internal Grid CreateGroupMenuHeader(PinnedGroupDefinition definition)
        {
            var counts = GroupCountsProvider(definition.Id);
            return new()
            {
                ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
                {
                    new TextBlock
                    {
                        Text = definition.Name.Length == 0 ? UiText.Get("PinnedGroupDefault") : definition.Name,
                    },
                    new TextBlock
                    {
                        Text = $"{counts.Visible}/{counts.Total}",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        [Grid.ColumnProperty] = 1,
                    },
                },
            };
        }
    }
}
