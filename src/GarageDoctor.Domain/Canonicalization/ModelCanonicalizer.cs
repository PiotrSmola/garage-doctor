namespace GarageDoctor.Domain.Canonicalization;

public sealed class ModelCanonicalizer
{
    public CanonicalName Canonicalize(string rawModel)
    {
        var normalized = RawText.Normalize(rawModel);
        return normalized.Length == 0 ? CanonicalName.Unknown : CanonicalName.From(normalized);
    }
}
