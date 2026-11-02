namespace RitsukageGif.Desktop
{
    internal sealed class PinnedHtmlFormatException(Exception innerException) : FormatException(
        "HTML content cannot be parsed within the supported document nesting limit.", innerException);
}
