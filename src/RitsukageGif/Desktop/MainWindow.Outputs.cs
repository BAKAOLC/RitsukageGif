using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RitsukageGif.Desktop
{
    public sealed partial class MainWindow
    {
        private readonly ComboBox _outputSelector = new()
        {
            IsVisible = false, MinHeight = 0, Height = 24, FontSize = 12,
            Padding = new Thickness(4, 0), Margin = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        private string[] _displayOutputs = [];
        private string? _selectedOutputPath;
        private bool _updatingOutputs;

        private void ConfigureOutputSelector()
        {
            ToolTip.SetTip(_outputSelector, UiText.Get("OutputPreview"));
            _outputSelector.SelectionChanged += async (_, _) =>
            {
                if (_updatingOutputs || _outputSelector.SelectedItem is not ComboBoxItem { Tag: string path }) return;
                _selectedOutputPath = path;
                RefreshFromHost();
                await LoadPreviewAsync(path);
            };
        }

        private void RefreshOutputs()
        {
            var outputs = _host.LastOutputs;
            if (_displayOutputs.SequenceEqual(outputs, StringComparer.Ordinal)) return;
            _displayOutputs = outputs.ToArray();
            _updatingOutputs = true;
            try
            {
                var selectedIndex = Array.FindIndex(_displayOutputs, path => path == _selectedOutputPath);
                if (selectedIndex < 0 && outputs.Count > 0) selectedIndex = 0;
                _selectedOutputPath = selectedIndex < 0 ? null : outputs[selectedIndex];
                _outputSelector.ItemsSource = outputs.Select(path =>
                {
                    var item = new ComboBoxItem
                    {
                        Tag = path,
                        Content = new TextBlock
                        {
                            Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    };
                    ToolTip.SetTip(item, path);
                    return item;
                }).ToArray();
                _outputSelector.SelectedIndex = selectedIndex;
                _outputSelector.IsVisible = outputs.Count > 1;
            }
            finally
            {
                _updatingOutputs = false;
            }
        }

        private void SelectOutput(string path)
        {
            RefreshOutputs();
            _selectedOutputPath = path;
            _updatingOutputs = true;
            try
            {
                _outputSelector.SelectedIndex = Array.FindIndex(_displayOutputs, value => value == path);
            }
            finally
            {
                _updatingOutputs = false;
            }

            RefreshFromHost();
        }

        private void RefreshStreamStatus()
        {
            var streams = _host.Streams;
            ToolTip.SetTip(_status, streams.Count == 0
                ? _host.Status
                : string.Join(Environment.NewLine,
                    streams.Select(stream =>
                    {
                        var state = UiText.Get(stream.State switch
                        {
                            "recording" => "StageRecording",
                            "encoding" => "StageEncoding",
                            "complete" => "RecordingComplete",
                            "cancelled" => "Cancelled",
                            _ => "RecordingFailed"
                        });
                        return $"{stream.Name}: {state} · {stream.Encoded} / {stream.Captured}" +
                               (stream.Error is { Length: > 0 } error ? $" · {error}" : "");
                    })));
        }
    }
}
