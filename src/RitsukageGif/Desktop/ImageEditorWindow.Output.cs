namespace RitsukageGif.Desktop
{
    internal sealed partial class ImageEditorWindow
    {
        private async Task<byte[]> ComposeSdrAsync()
        {
            await _previewGate.WaitAsync(_lifetime.Token);
            try
            {
                return await Session.ComposeAsync(_sourceBounds, _sourcePng, _lifetime.Token);
            }
            finally
            {
                _previewGate.Release();
            }
        }

        private async Task<ScreenshotResult> ComposeResultAsync(ScreenshotOutputAction action,
            CancellationToken token)
        {
            await _previewGate.WaitAsync(token);
            try
            {
                var png = await Session.ComposeAsync(_sourceBounds, _sourcePng, token);
                var hdr = _hdrSource;
                if (hdr is not null && Session.HasAnnotations)
                    hdr = await Session.ComposeHdrAsync(_sourceBounds, hdr, token: token);
                return new(png, action, hdr)
                {
                    ColorMode = Session.OutputColorMode, CaptureScaling = 1,
                };
            }
            finally
            {
                _previewGate.Release();
            }
        }

        private async Task CompleteAsync(ScreenshotOutputAction action)
        {
            if (_closed || _outputBusy) return;
            _outputBusy = true;
            Session.SetExportState(true);
            try
            {
                var result = await ComposeResultAsync(action, _lifetime.Token);
                if (await _output(this, result, _lifetime.Token) && !_closed) Close();
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                SetEditorError(error);
            }
            finally
            {
                _outputBusy = false;
                if (!_closed) Session.SetExportState(false);
            }
        }
    }
}
