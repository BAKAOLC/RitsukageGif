namespace RitsukageGif.Desktop
{
    internal interface IStartupRegistration
    {
        string? Read();
        void Write(string? command);
    }
}
