namespace RitsukageGif.Desktop
{
    public sealed partial class ApplicationHost
    {
        private SaveNotificationWindow? _saveNotification;

        private void ShowOutputNotification(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0 || IsClosing) return;
            SetStatus(UiText.Format("AnimationExported", string.Join(", ", paths)));
            if (!_startServices) return;
            if (_saveNotification is null)
            {
                var notification = new SaveNotificationWindow(SetStatus);
                notification.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_saveNotification, notification)) _saveNotification = null;
                };
                _saveNotification = notification;
            }

            var screen = View is { IsVisible: true } view
                ? view.Screens.ScreenFromWindow(view)
                : _services.Context.Screens.Primary;
            _saveNotification.Present(paths, screen);
        }

        private void CloseOutputNotification()
        {
            var notification = _saveNotification;
            _saveNotification = null;
            notification?.Close();
        }
    }
}
