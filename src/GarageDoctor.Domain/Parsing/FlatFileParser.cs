using System.Runtime.CompilerServices;
using System.Text;

namespace GarageDoctor.Domain.Parsing;

public sealed class FlatFileParser
{
    private const char FieldSeparator = '\t';
    private const char CarriageReturn = '\r';
    private const long ProgressReportInterval = 100_000;
    private const int StreamBufferSize = 64 * 1024;

    private static readonly UTF8Encoding Utf8WithoutByteOrderMark = new(encoderShouldEmitUTF8Identifier: false);

    public FlatFileParser(int expectedFieldCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedFieldCount, 1);
        ExpectedFieldCount = expectedFieldCount;
    }

    public static FlatFileParser ForComplaints() => new(ComplaintFields.FieldCount);

    public static FlatFileParser ForRecalls() => new(RecallFields.FieldCount);

    public int ExpectedFieldCount { get; }

    public long LinesRead { get; private set; }

    public long BlankLinesSkipped { get; private set; }

    public async IAsyncEnumerable<string[]> ReadRecordsAsync(
        string path,
        IProgress<long>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        LinesRead = 0;
        BlankLinesSkipped = 0;

        var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using (source.ConfigureAwait(false))
        {
            using var reader = new StreamReader(
                source,
                Utf8WithoutByteOrderMark,
                detectEncodingFromByteOrderMarks: true,
                StreamBufferSize,
                leaveOpen: true);

            var lineNumber = 0L;
            var nextProgressReport = ProgressReportInterval;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;

                var line = WithoutTrailingCarriageReturn(rawLine);

                if (string.IsNullOrWhiteSpace(line))
                {
                    BlankLinesSkipped++;
                    continue;
                }

                var fields = line.Split(FieldSeparator);

                if (fields.Length != ExpectedFieldCount)
                {
                    throw new FlatFileFormatException(path, lineNumber, ExpectedFieldCount, fields.Length);
                }

                LinesRead++;

                if (LinesRead >= nextProgressReport)
                {
                    progress?.Report(LinesRead);
                    nextProgressReport += ProgressReportInterval;
                }

                yield return fields;
            }
        }
    }

    private static string WithoutTrailingCarriageReturn(string line) =>
        line.Length > 0 && line[^1] == CarriageReturn ? line[..^1] : line;
}
