namespace GarageDoctor.Ingest;

public sealed class IngestArguments
{
    public bool Resume { get; init; }

    public bool SkipRecalls { get; init; }

    /// <summary>
    /// Skip the flat files entirely and recompute the catalogue, the indexes, the profiles and the
    /// component taxonomy from the complaints and recalls already in MongoDB.
    /// </summary>
    public bool RebuildOnly { get; init; }

    public long? RecordLimit { get; init; }

    public string DataDirectory { get; init; } = "data";

    public static IngestArguments Parse(string[] args)
    {
        var resume = false;
        var skipRecalls = false;
        var rebuildOnly = false;
        long? recordLimit = null;
        var dataDirectory = "data";

        foreach (var argument in args)
        {
            if (argument.Equals("--resume", StringComparison.OrdinalIgnoreCase))
            {
                resume = true;
            }
            else if (argument.Equals("--skip-recalls", StringComparison.OrdinalIgnoreCase))
            {
                skipRecalls = true;
            }
            else if (argument.Equals("--rebuild-only", StringComparison.OrdinalIgnoreCase))
            {
                rebuildOnly = true;
            }
            else if (argument.StartsWith("--limit=", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(argument["--limit=".Length..], out var limit))
            {
                recordLimit = limit;
            }
            else if (argument.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
            {
                dataDirectory = argument["--data-dir=".Length..];
            }
        }

        return new IngestArguments
        {
            Resume = resume,
            SkipRecalls = skipRecalls,
            RebuildOnly = rebuildOnly,
            RecordLimit = recordLimit,
            DataDirectory = dataDirectory
        };
    }
}
