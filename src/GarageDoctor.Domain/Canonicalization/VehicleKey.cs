using System.Text;

namespace GarageDoctor.Domain.Canonicalization;

public static class VehicleKey
{
    public const string Unknown = "unknown";

    public static string Create(string makeSlug, string modelSlug, int? modelYear) =>
        string.Concat(makeSlug, "|", modelSlug, "|", modelYear?.ToString() ?? Unknown);

    public static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }
                pendingSeparator = false;
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                pendingSeparator = true;
            }
        }
        return builder.Length == 0 ? Unknown : builder.ToString();
    }
}
