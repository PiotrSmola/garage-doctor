using System.Text;

namespace GarageDoctor.Domain.Canonicalization;

public sealed record CanonicalName(string Name, string Slug)
{
    public const string UnknownName = "UNKNOWN";

    public static CanonicalName Unknown { get; } = new(UnknownName, VehicleKey.Unknown);

    public static CanonicalName From(string name)
    {
        var slug = VehicleKey.Slug(name);
        return slug == VehicleKey.Unknown ? Unknown : new CanonicalName(name, slug);
    }
}

internal static class RawText
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var source = value.AsSpan().Trim();
        var builder = new StringBuilder(source.Length);
        var pendingSeparator = false;

        foreach (var character in source)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                builder.Append(' ');
                pendingSeparator = false;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
