using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private static readonly ILogger DocumentOutputLogger = LogHub.GetLogger("Desktop.RecognitionOutput");
        private readonly RecognitionDocumentOutput _documentOutput;
        private readonly Button _formulaOpenWord = new();
        private bool _tablePrintCapabilityPending;

        private void AddTablePrintItem(ContextMenu menu)
        {
            var item = new MenuItem();
            ToolTip.SetShowOnDisabled(item, true);
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, LiveText.Resource("Print"));
            item.Icon = ToolbarIcon.Create(item, ToolbarIcon.GetDefinition("Print"));
            item.Click += (_, _) => _operation = PrintTableAsync();
            menu.Opening += (_, _) =>
            {
                item.IsEnabled = !_busy && !_lifetime.IsCancellationRequested && _table.Document is not null &&
                                 _documentOutput.CanPrint;
                ToolTip.SetTip(item, _documentOutput.CanPrint ? null : UiText.Get("PrintUnavailable"));
                _ = RefreshTablePrintCapabilitiesAsync(item);
            };
            menu.Items.Add(item);
        }

        private async Task RefreshTablePrintCapabilitiesAsync(MenuItem item)
        {
            if (_tablePrintCapabilityPending || _lifetime.IsCancellationRequested) return;
            _tablePrintCapabilityPending = true;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            work.CancelAfter(TimeSpan.FromSeconds(6));
            try
            {
                var capabilities = await _documentOutput.GetPrintCapabilitiesAsync(work.Token);
                if (_lifetime.IsCancellationRequested) return;
                item.IsEnabled = !_busy && _table.Document is not null && capabilities.Available;
                ToolTip.SetTip(item, capabilities.Available ? null : UiText.Get("PrintUnavailable"));
                if (!capabilities.Available)
                    DocumentOutputLogger.ZLogDebug(
                        $"Native table printing unavailable. Detail={capabilities.FailureReason}");
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                DocumentOutputLogger.ZLogDebug(error, $"Native table print availability query failed.");
            }
            finally
            {
                _tablePrintCapabilityPending = false;
            }
        }

        private Task PrintTableAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested ||
                TableForOutput() is not { } table)
                return Task.CompletedTask;
            var owner = TryGetPlatformHandle()?.Handle ?? 0;
            return RunDocumentOutputAsync(async token =>
            {
                try
                {
                    var printed = await _documentOutput.PrintTableAsync(owner, table, TablePrintRenderer.DefaultOptions,
                        UiText.Get("TableRecognition"), token);
                    if (!printed && !_lifetime.IsCancellationRequested)
                        _status.Text = UiText.Get("RecognitionCancelled");
                }
                catch (PlatformNotSupportedException)
                {
                    if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("PrintUnavailable");
                }
            });
        }

        private Button CreateFormulaOpenWordButton()
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, LiveText.Resource("FormulaOpenWord"));
            _formulaOpenWord.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children = { ToolbarIcon.Create(_formulaOpenWord, ToolbarIcon.GetDefinition("WordDocument")), label },
            };
            _formulaOpenWord.Click += (_, _) => _operation = OpenFormulaWordAsync();
            return _formulaOpenWord;
        }

        private Task SaveFormulaDocxAsync(IReadOnlyList<string> sources, string path)
        {
            return RunDocumentOutputAsync(async token =>
            {
                var mathMl = await ConvertFormulaDocumentSourcesAsync(sources, token);
                await _documentOutput.SaveFormulaDocxAsync(mathMl, path, token);
            });
        }

        private Task OpenFormulaWordAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested) return Task.CompletedTask;
            var sources = FormulaSourcesForOutput();
            if (sources.Length == 0) return Task.CompletedTask;
            return RunDocumentOutputAsync(async token =>
            {
                var mathMl = await ConvertFormulaDocumentSourcesAsync(sources, token);
                var result = await _documentOutput.OpenFormulaDocxAsync(mathMl, token);
                ShowDocumentOpenResult(result);
            });
        }

        private Task OpenTableExcelAsync()
        {
            if (_busy || _lifetime.IsCancellationRequested || TableForOutput() is not { } table)
                return Task.CompletedTask;
            return RunDocumentOutputAsync(async token =>
            {
                var result = await _documentOutput.OpenTableAsync(table, token);
                ShowDocumentOpenResult(result);
            });
        }

        private static async Task<IReadOnlyList<string>> ConvertFormulaDocumentSourcesAsync(
            IReadOnlyList<string> sources,
            CancellationToken token)
        {
            var mathMl = new string[sources.Count];
            for (var index = 0; index < sources.Count; index++)
            {
                var converted =
                    await StructuredRecognitionProcess.Shared.ConvertAsync(sources[index], FormulaTextFormat.MathMl,
                        token);
                token.ThrowIfCancellationRequested();
                if (converted.ValidationError is { } error) throw new InvalidDataException(error);
                mathMl[index] = converted.Text ??
                                throw new InvalidDataException("The formula conversion returned no MathML.");
            }

            return mathMl;
        }

        private async Task RunDocumentOutputAsync(Func<CancellationToken, Task> output)
        {
            if (_busy || _lifetime.IsCancellationRequested) return;
            using var work = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _work = work;
            SetBusy(true);
            _status.Text = "";
            try
            {
                DocumentOutputLogger.ZLogDebug($"Recognition document output started.");
                await output(work.Token);
                DocumentOutputLogger.ZLogDebug($"Recognition document output completed.");
            }
            catch (OperationCanceledException) when (work.IsCancellationRequested)
            {
                DocumentOutputLogger.ZLogDebug($"Recognition document output canceled.");
                if (!_lifetime.IsCancellationRequested) _status.Text = UiText.Get("RecognitionCancelled");
            }
            catch (Exception error)
            {
                DocumentOutputLogger.ZLogWarning(
                    $"Recognition document output failed. ErrorType={error.GetType().Name} Code={error.HResult}");
                if (!_lifetime.IsCancellationRequested) _status.Text = error.Message;
            }
            finally
            {
                _work = null;
                SetBusy(false);
            }
        }

        private void ShowDocumentOpenResult(DocumentOpenResult result)
        {
            if (!_lifetime.IsCancellationRequested && !result.Opened)
            {
                DocumentOutputLogger.ZLogDebug($"Default application handoff failed. ErrorType={result.Error}");
                _status.Text = UiText.Format("DocumentOpenFailed", result.Path);
            }
        }
    }
}
