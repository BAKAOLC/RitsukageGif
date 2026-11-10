using System.Numerics;

namespace RitsukageGif.Core
{
    public static class PrivacyCandidateGrouping
    {
        public static bool HasSimilarAppearance(PrivacyCandidate first, PrivacyCandidate second)
        {
            if (!first.IsImageStructure || second.Kind != first.Kind ||
                first.AppearanceSignature is not { } left || second.AppearanceSignature is not { } right ||
                first.Bounds.IsEmpty || second.Bounds.IsEmpty) return false;
            var firstRatio = (double)first.Bounds.Width / first.Bounds.Height;
            var secondRatio = (double)second.Bounds.Width / second.Bounds.Height;
            if (Math.Max(firstRatio, secondRatio) / Math.Min(firstRatio, secondRatio) > 1.1) return false;
            return BitOperations.PopCount(left ^ right) <= 8;
        }
    }
}
