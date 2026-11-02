using Avalonia.Media.Imaging;

namespace RitsukageGif.Desktop
{
    internal sealed class PinnedRichResourceEntry(string key, Uri? address, long id)
    {
        internal string Key { get; } = key;
        internal Uri? Address { get; } = address;
        internal string Scheme => Address?.Scheme ?? "data";
        internal long Id { get; } = id;
        internal PinnedRichResourceAsset? Asset { get; set; }
        internal Bitmap? Bitmap { get; set; }
        internal string? Error { get; set; }
        internal long DecodedBytes { get; set; }
        internal int Leases { get; set; }
        internal int Waiters { get; set; }
        internal TaskCompletionSource? Pending { get; set; }
        internal LinkedListNode<PinnedRichResourceEntry>? IdleNode { get; set; }
    }
}
