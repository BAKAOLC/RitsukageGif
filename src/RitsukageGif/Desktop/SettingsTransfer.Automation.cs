using System.Text;

namespace RitsukageGif.Desktop
{
    internal static partial class SettingsTransfer
    {
        private static readonly UTF8Encoding AutomationUtf8 = new(false, true);

        internal static AppSettings ImportJson(string json, CancellationToken token)
        {
            ValidateJsonSize(json);
            return Import(AutomationUtf8.GetBytes(json), token);
        }

        internal static void ValidateJsonSize(string json)
        {
            if (AutomationUtf8.GetByteCount(json) > MaximumBytes)
                throw new InvalidDataException(UiText.Get("SettingsTransferTooLarge"));
        }
    }
}
