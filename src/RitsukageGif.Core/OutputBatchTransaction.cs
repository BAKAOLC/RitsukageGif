namespace RitsukageGif.Core
{
    public static class OutputBatchTransaction
    {
        public static void Publish(IReadOnlyList<string> staged, IReadOnlyList<string> destinations, bool overwrite,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(staged);
            ArgumentNullException.ThrowIfNull(destinations);
            if (staged.Count == 0 || staged.Count != destinations.Count)
                throw new ArgumentException(null, nameof(destinations));
            token.ThrowIfCancellationRequested();
            VideoOutputTransaction.Publish(staged, destinations, overwrite);
        }
    }
}
