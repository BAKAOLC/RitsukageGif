using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class StructuredRecognitionWindow
    {
        private MenuFlyout CreateFormulaCopyMenu()
        {
            var menu = new MenuFlyout();
            foreach (var format in Enum.GetValues<FormulaCopyFormat>())
            {
                var item = new MenuItem
                {
                    Header = format switch
                    {
                        FormulaCopyFormat.Latex => "LaTeX", FormulaCopyFormat.MathMl => "MathML",
                        FormulaCopyFormat.AsciiMath => "AsciiMath", FormulaCopyFormat.Typst => "Typst",
                        _ => "",
                    },
                    ToggleType = MenuItemToggleType.Radio, GroupName = "formula-copy-format",
                    IsChecked = _copyFormat == format,
                };
                if (format is FormulaCopyFormat.Text or FormulaCopyFormat.Image)
                    item.Bind(HeaderedSelectingItemsControl.HeaderProperty,
                        LiveText.Resource(format == FormulaCopyFormat.Text ? "FormulaCopyText" : "FormulaCopyImage"));
                item.Click += (_, _) =>
                {
                    if (_lifetime.IsCancellationRequested) return;
                    _copyFormat = format;
                    _copyFormatChanged?.Invoke(format);
                };
                menu.Items.Add(item);
            }

            return menu;
        }

        private async Task CopyFormulaAsync()
        {
            var token = _lifetime.Token;
            var clipboard = Clipboard ?? throw new InvalidOperationException(UiText.Get("ClipboardFailed"));
            if (_latex.SelectedText.Length > 0)
            {
                await clipboard.SetTextAsync(_latex.SelectedText);
                await clipboard.FlushAsync();
                return;
            }

            var sources = FormulaSourcesForOutput();
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (_copyFormat is FormulaCopyFormat.Latex or FormulaCopyFormat.Text)
            {
                var text = string.Join("\n", FormulaDisplaySourcesForOutput());
                if (_copyFormat == FormulaCopyFormat.Latex)
                {
                    await clipboard.SetTextAsync(text);
                }
                else
                {
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.CreateText(text));
                    await clipboard.SetDataAsync(transfer);
                }

                await clipboard.FlushAsync();
                return;
            }

            if (_copyFormat == FormulaCopyFormat.Image)
            {
                var image = await StructuredRecognitionProcess.Shared.RenderManyPngAsync(sources, token: token);
                token.ThrowIfCancellationRequested();
                if (image.ValidationError is { } imageError)
                {
                    _status.Text = imageError;
                    return;
                }

                await ClipboardImage.CopyAsync(clipboard, image.Png!, token);
                return;
            }

            var target = _copyFormat switch
            {
                FormulaCopyFormat.Latex => FormulaTextFormat.Latex, FormulaCopyFormat.Text => FormulaTextFormat.Text,
                FormulaCopyFormat.Image => throw new InvalidOperationException(
                    "Image copies are handled before text conversion."),
                FormulaCopyFormat.MathMl => FormulaTextFormat.MathMl,
                FormulaCopyFormat.AsciiMath => FormulaTextFormat.AsciiMath,
                FormulaCopyFormat.Typst => FormulaTextFormat.Typst,
                _ => throw new ArgumentOutOfRangeException(),
            };
            var converted = await StructuredRecognitionProcess.Shared.ConvertManyAsync(sources, target, token);
            token.ThrowIfCancellationRequested();
            if (converted.ValidationError is { } error)
            {
                _status.Text = error;
                return;
            }

            if (_copyFormat == FormulaCopyFormat.MathMl)
            {
                var data = new DataTransfer();
                var item = DataTransferItem.CreateText(converted.Text!);
                item.Set(ClipboardPlatformFormats.CreateBytesFormat("application/mathml+xml"),
                    Encoding.UTF8.GetBytes(converted.Text!));
                data.Add(item);
                await clipboard.SetDataAsync(data);
            }
            else
            {
                await clipboard.SetTextAsync(converted.Text!);
            }

            await clipboard.FlushAsync();
        }
    }
}
