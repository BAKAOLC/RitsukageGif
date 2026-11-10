namespace RitsukageGif.Desktop
{
    internal static partial class MacWindowSystem
    {
        internal static async Task<nint> ActiveWindowHandleAsync(CancellationToken token)
        {
            using var result = await ReadCommandAsync("active-window", token).ConfigureAwait(false);
            return (nint)result.RootElement.GetProperty("handle").GetInt64();
        }
    }
}
