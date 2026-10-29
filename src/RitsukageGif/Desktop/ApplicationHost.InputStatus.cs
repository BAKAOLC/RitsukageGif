namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private InputConfigurationStatusSource _inputStatusSource;

        private void SetInputConfigurationStatus(InputConfigurationStatusSource source, string message)
        {
            _inputStatusSource = source;
            _statusKey = null;
            _status = message;
            Notify();
        }

        private void ClearInputConfigurationStatus(InputConfigurationStatusSource source)
        {
            if (_inputStatusSource != source) return;
            _inputStatusSource = InputConfigurationStatusSource.None;
            _statusKey = "Ready";
            Notify();
        }

        private void ClearValidatedInputConfigurationStatus(bool selectionValid, bool mouseValid, bool actionsValid)
        {
            if ((_inputStatusSource == InputConfigurationStatusSource.SelectionShortcuts && selectionValid) ||
                (_inputStatusSource == InputConfigurationStatusSource.MouseShortcuts && mouseValid) ||
                (_inputStatusSource == InputConfigurationStatusSource.GlobalActions && actionsValid))
                ClearInputConfigurationStatus(_inputStatusSource);
        }
    }
}
