using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace RitsukageGif.Desktop
{
    internal sealed partial class AnimationEditorWindow
    {
        private Task _activeOperation = Task.CompletedTask;

        private async Task RunAsync(Func<CancellationToken, Task> action)
        {
            if (_operation is not null) return;
            _playback.Stop();
            using var cancellation = new CancellationTokenSource();
            _operation = cancellation;
            _editing.IsEnabled = false;
            _cancel.IsVisible = true;
            SetStatus("AnimationWorking");
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeOperation = completion.Task;
            try
            {
                await _frameLoad;
                cancellation.Token.ThrowIfCancellationRequested();
                await action(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled");
            }
            catch (Exception exception)
            {
                SetStatusError(exception.Message);
            }
            finally
            {
                _operation = null;
                _editing.IsEnabled = true;
                _cancel.IsVisible = false;
                completion.SetResult();
            }
        }

        private Task MutateAsync(Action<AnimationEditDocument> action) => RunAsync(async token =>
        {
            if (_document is null) return;
            if (_canvas.HasChanges)
            {
                SetStatus("AnimationApplyFirst");
                return;
            }

            var before = _document.Frames;
            var iterations = _document.Iterations;
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                action(_document);
            }, token);
            if (!ReferenceEquals(before, _document.Frames) || iterations != _document.Iterations) _dirty = true;
            Refresh();
        });

        private Task EditAsync(Action<AnimationEditDocument, CancellationToken> action) => RunAsync(async token =>
        {
            if (_document is null) return;
            if (_canvas.HasChanges)
            {
                SetStatus("AnimationApplyFirst");
                return;
            }

            var before = _document.Frames;
            try
            {
                await Task.Run(() => action(_document, token), token);
            }
            finally
            {
                if (!ReferenceEquals(before, _document.Frames))
                {
                    _dirty = true;
                    Refresh();
                }
            }
        });

        private Task UndoAsync()
        {
            if (_canvas.CanUndoDraft)
            {
                _canvas.UndoDraft();
                return Task.CompletedTask;
            }

            return MutateAsync(d => d.Undo());
        }

        private Task RedoAsync()
        {
            if (_canvas.CanRedoDraft)
            {
                _canvas.RedoDraft();
                return Task.CompletedTask;
            }

            return MutateAsync(d => d.Redo());
        }

        private async Task MoveFrameAsync(int offset)
        {
            var frames = SelectedFrames();
            if (!HasFrameSelection(frames)) return;
            var primary = Math.Max(0, Array.IndexOf(frames, CurrentFrame));
            int[]? moved = null;
            await MutateAsync(d => moved = d.MoveSelected(frames, offset));
            if (moved is null || moved.Length == 0) return;
            var current = moved[Math.Min(primary, moved.Length - 1)];
            var selection = new List<int>(moved.Length) { current };
            selection.AddRange(moved.Where(index => index != current));
            _refreshing = true;
            try
            {
                _frames.SelectedItems = selection;
            }
            finally
            {
                _refreshing = false;
            }

            ShowFrame();
            _frames.ScrollIntoView(current);
        }

        private static FilePickerFileType AnimationFiles() => new(UiText.Get("AnimationFiles"))
            { Patterns = ["*.gif", "*.webp", "*.apng", "*.png", "*.jpg", "*.jpeg", "*.bmp"] };

        internal async Task OpenPathAsync(string path)
        {
            if (_operation is not null) return;
            if (await ConfirmDiscardAsync()) await LoadAsync(path);
        }

        private async Task OpenAsync()
        {
            if (!await ConfirmDiscardAsync()) return;
            var files = await StorageProvider.OpenFilePickerAsync(new()
                { Title = UiText.Get("EditorOpen"), FileTypeFilter = [AnimationFiles()] });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await LoadAsync(path);
        }

        private Task LoadAsync(string path) => RunAsync(async token =>
        {
            var loaded = await Task.Run(() => AnimationEditDocument.Open(path, token), token);
            try
            {
                await _canvas.LoadFrameAsync(loaded.Frames[0].Path, token);
            }
            catch
            {
                loaded.Dispose();
                throw;
            }

            _displayedPath = loaded.Frames[0].Path;
            _document?.Dispose();
            _document = loaded;
            _path = path;
            _dirty = false;
            Refresh();
        });

        private async Task NewAsync()
        {
            if (!await ConfirmDiscardAsync()) return;
            var width = Value(_width);
            var height = Value(_height);
            var delay = Value(_delay);
            await RunAsync(async token =>
            {
                var document = await Task.Run(() => AnimationEditDocument.Create(width, height, delay), token);
                try
                {
                    await _canvas.LoadFrameAsync(document.Frames[0].Path, token);
                }
                catch
                {
                    document.Dispose();
                    throw;
                }

                _displayedPath = document.Frames[0].Path;
                _document?.Dispose();
                _document = document;
                _path = null;
                _dirty = true;
                Refresh();
            });
        }

        private async Task ImportAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
                { Title = UiText.Get("AnimationImport"), AllowMultiple = true, FileTypeFilter = [AnimationFiles()] });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            var index = CurrentFrame + 1;
            if (paths.Length > 0)
                await EditAsync((d, token) =>
                {
                    foreach (var path in paths)
                    {
                        var count = d.Frames.Count;
                        d.Import(path, index, token);
                        index += d.Frames.Count - count;
                    }
                });
        }

        private async Task ExportAsync()
        {
            if (_document is null) return;
            if (_canvas.HasChanges)
            {
                SetStatus("AnimationApplyFirst");
                return;
            }

            var formats = new[] { "gif", "webp", "apng", "png" };
            var format = formats[Math.Max(0, _format.SelectedIndex)];
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = UiText.Get("AnimationExport"),
                SuggestedFileName =
                    $"{(_path is null ? "animation" : Path.GetFileNameWithoutExtension(_path))}-edited.{format}",
                DefaultExtension = format, ShowOverwritePrompt = true,
                FileTypeChoices = [new(format.ToUpperInvariant()) { Patterns = [$"*.{format}"] }]
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            var selectedFrame = CurrentFrame;
            var options = new MediaProcessOptions
            {
                Format = format, Quality = Value(_exportQuality), Colors = Value(_colors),
                Lossless = _lossless.IsChecked == true, Dither = _dither.IsChecked == true, Overwrite = true
            };
            await RunAsync(async token =>
            {
                if (format == "png") await Task.Run(() => _document.ExportFrame(selectedFrame, path, token), token);
                else await Task.Run(() => _document.ExportAsync(path, options, token), token);
                if (format != "png" || _document.Frames.Count == 1)
                {
                    _dirty = false;
                    _path = path;
                }

                Refresh();
                SetStatus("AnimationExported", path);
            });
        }

        private async Task<bool> ConfirmDiscardAsync()
        {
            if (!_dirty && !_canvas.HasChanges) return true;
            var dialog = new Window
            {
                [!TitleProperty] = LiveText.Resource("AnimationEditorTitle"), Width = 390,
                SizeToContent = SizeToContent.Height,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new(16), Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                [!TextBlock.TextProperty] = LiveText.Resource("AnimationUnsaved"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            var buttons = new StackPanel
                { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var discard = new Button { [!ContentControl.ContentProperty] = LiveText.Resource("EditorDiscard") };
            var cancel = new Button { [!ContentControl.ContentProperty] = LiveText.Resource("EditorCancel") };
            discard.Click += (_, _) => dialog.Close(true);
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(discard);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            return await dialog.ShowDialog<bool>(this);
        }

        internal async Task<bool> RequestCloseAsync()
        {
            if (_allowClose) return true;
            if (_confirming) return false;
            _confirming = true;
            try
            {
                _operation?.Cancel();
                await _activeOperation;
                await _frameLoad;
                if (!await ConfirmDiscardAsync()) return false;
                _allowClose = true;
                Close();
                return true;
            }
            finally
            {
                _confirming = false;
            }
        }

        private async void OnClosing(object? sender, WindowClosingEventArgs args)
        {
            if (_allowClose) return;
            args.Cancel = true;
            await RequestCloseAsync();
        }
    }
}
