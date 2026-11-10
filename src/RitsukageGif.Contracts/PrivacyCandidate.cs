using System.Text.Json.Serialization;

namespace RitsukageGif.Contracts
{
    public sealed record PrivacyCandidate(
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string ProviderId,
        PrivacyCandidateKind Kind,
        PixelRect Bounds,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        string Text,
        // ReSharper disable once NotAccessedPositionalProperty.Global
        bool BoundsApproximate = false)
    {
        public ulong? AppearanceSignature { get; init; }

        [JsonIgnore]
        public bool IsImageStructure => Kind is PrivacyCandidateKind.ImageStructure or PrivacyCandidateKind.ImageBlock
            or PrivacyCandidateKind.RelatedStructure;

        public bool IsSensitive => Kind is PrivacyCandidateKind.Email or PrivacyCandidateKind.Phone
            or PrivacyCandidateKind.IdentityNumber or PrivacyCandidateKind.PaymentCard or PrivacyCandidateKind.QrCode
            or PrivacyCandidateKind.ImageBlock or PrivacyCandidateKind.RelatedStructure;
    }
}
