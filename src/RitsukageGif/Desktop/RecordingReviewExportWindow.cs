using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace RitsukageGif.Desktop
{
    internal sealed class RecordingReviewExportWindow : Window
    {
        private readonly CancellationTokenSource _cancellation;

        private readonly RecordingReviewProgressValue _reported = new();

        private readonly ProgressBar _progress = new()
            { Minimum = 0, Maximum = 100, Height = 20, ShowProgressText = true };
        private readonly Grid _buttons = new() { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 6 };
        private readonly CheckBox _autoExit = new() { IsVisible = false };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
        private readonly Func<IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> _encode;
        private Task _operation = Task.CompletedTask;
        private IReadOnlyList<string> _outputs = [];
        private Exception? _failure;
        private bool _canClose;

        private RecordingReviewExportWindow(bool autoExit,
            Func<IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> encode,
            Action<bool>? autoExitChanged, CancellationToken token)
        {
            _encode = encode;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Title = UiText.Get("RecordingReviewExporting");
            Width = 520;
            CanResize = false;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _autoExit.Content = UiText.Get("RecordingReviewAutoExit");
            _autoExit.IsChecked = autoExit;
            _autoExit.IsCheckedChanged += (_, _) => autoExitChanged?.Invoke(_autoExit.IsChecked == true);
            var minimize = new Button { Content = UiText.Get("RecordingReviewMinimize") };
            minimize.Click += (_, _) => WindowState = WindowState.Minimized;
            var cancel = new Button { Content = UiText.Get("RecordingReviewCancel") };
            cancel.Click += (_, _) => _cancellation.Cancel();
            _buttons.Children.Add(minimize);
            Grid.SetColumn(cancel, 2);
            _buttons.Children.Add(cancel);
            Content = new StackPanel { Spacing = 8, Margin = new(12), Children = { _progress, _autoExit, _buttons } };
            _timer.Tick += (_, _) => _progress.Value = _reported.Value * 100;
            Opened += (_, _) =>
            {
                _timer.Start();
                _operation = EncodeAsync();
            };
            Closing += (_, args) =>
            {
                if (_canClose) return;
                args.Cancel = true;
                _cancellation.Cancel();
            };
            KeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                if (_canClose) Close();
                else _cancellation.Cancel();
            };
        }

        internal static async Task<RecordingReviewExportResult?> ShowAsync(Window owner, bool autoExit,
            Func<IProgress<double>, CancellationToken, Task<IReadOnlyList<string>>> encode,
            Action<bool>? autoExitChanged, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var dialog = new RecordingReviewExportWindow(autoExit, encode, autoExitChanged, token);
            await using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsVisible) dialog.Close();
            }));
            try
            {
                var result = await dialog.ShowDialog<RecordingReviewExportResult?>(owner);
                await dialog._operation;
                if (dialog._failure is { } error) ExceptionDispatchInfo.Capture(error).Throw();
                return result ?? (dialog._outputs.Count == 0
                    ? null
                    : new RecordingReviewExportResult(dialog._outputs, false, dialog._autoExit.IsChecked == true));
            }
            finally
            {
                dialog._timer.Stop();
                dialog._cancellation.Dispose();
            }
        }

        private async Task EncodeAsync()
        {
            try
            {
                _outputs = await _encode(_reported, _cancellation.Token);
                _timer.Stop();
                _progress.Value = 100;
                _canClose = true;
                if (_autoExit.IsChecked == true)
                {
                    Close(new RecordingReviewExportResult(_outputs, true, true));
                    return;
                }

                Title = UiText.Get("RecordingReviewExportComplete");
                _autoExit.IsVisible = true;
                _buttons.Children.Clear();
                _buttons.ColumnDefinitions = new("*,Auto,Auto");
                var exit = new Button { Content = UiText.Get("RecordingReviewExitRecord") };
                exit.Click += (_, _) =>
                    Close(new RecordingReviewExportResult(_outputs, true, _autoExit.IsChecked == true));
                var complete = new Button { Content = UiText.Get("RecordingReviewComplete") };
                complete.Click += (_, _) =>
                    Close(new RecordingReviewExportResult(_outputs, _autoExit.IsChecked == true,
                        _autoExit.IsChecked == true));
                Grid.SetColumn(exit, 1);
                Grid.SetColumn(complete, 2);
                _buttons.Children.Add(exit);
                _buttons.Children.Add(complete);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                _canClose = true;
                Close();
            }
            catch (Exception error)
            {
                _failure = error;
                _canClose = true;
                Close();
            }
        }
    }
}
