using System.Text;
using GarageDoctor.Domain.Parsing;
using GarageDoctor.Tests;

namespace GarageDoctor.UnitTests.Parsing;

public sealed class GeneratedFlatFile : IDisposable
{
    public const long LineCount = 200_000;

    public GeneratedFlatFile()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"garage-doctor-{Guid.NewGuid():N}.tsv");
        var line = "1" + new string('\t', ComplaintFields.FieldCount - 1);

        using var writer = new StreamWriter(Path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        for (var written = 0L; written < LineCount; written++)
        {
            writer.WriteLine(line);
        }
    }

    public string Path { get; }

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}

public sealed class FlatFileParserTests(GeneratedFlatFile generatedFile) : IClassFixture<GeneratedFlatFile>
{
    private const string ValidFixture = "complaints-valid.tsv";
    private const string EdgeFixture = "complaints-edge.tsv";
    private const string MalformedFixture = "complaints-malformed.tsv";
    private const string RecallsFixture = "recalls-sample.tsv";

    private const long ShortRowLineNumber = 3;
    private const long LongRowLineNumberWithoutShortRow = 4;

    [Fact]
    public void FactoryMethodsUseTheFieldCountsFromTheOfficialSpecifications()
    {
        Assert.Equal(ComplaintFields.FieldCount, FlatFileParser.ForComplaints().ExpectedFieldCount);
        Assert.Equal(RecallFields.FieldCount, FlatFileParser.ForRecalls().ExpectedFieldCount);
    }

    [Fact]
    public async Task ValidComplaintFixtureYieldsThirtyRecordsOfFiftyOneFields()
    {
        var parser = FlatFileParser.ForComplaints();
        var records = await ReadAllAsync(parser, TestPaths.Fixture(ValidFixture));

        Assert.Equal(30, records.Count);
        Assert.All(records, record => Assert.Equal(ComplaintFields.FieldCount, record.Length));
        Assert.Equal(30L, parser.LinesRead);
        Assert.Equal(0L, parser.BlankLinesSkipped);
    }

    [Fact]
    public async Task EdgeCaseFixtureParsesWithoutThrowing()
    {
        var parser = FlatFileParser.ForComplaints();
        var records = await ReadAllAsync(parser, TestPaths.Fixture(EdgeFixture));

        Assert.Equal(22, records.Count);
        Assert.All(records, record => Assert.Equal(ComplaintFields.FieldCount, record.Length));
    }

    [Fact]
    public async Task ShortRowThrowsWithLineNumberExpectedAndActualFieldCounts()
    {
        var path = TestPaths.Fixture(MalformedFixture);
        var parser = FlatFileParser.ForComplaints();

        var exception = await Assert.ThrowsAsync<FlatFileFormatException>(() => ReadAllAsync(parser, path));

        Assert.Equal(ShortRowLineNumber, exception.LineNumber);
        Assert.Equal(ComplaintFields.FieldCount, exception.ExpectedFieldCount);
        Assert.Equal(50, exception.ActualFieldCount);
        Assert.Equal(path, exception.FilePath);
        Assert.Contains(ShortRowLineNumber.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LongRowThrowsWithItsOwnActualFieldCount()
    {
        var lines = await File.ReadAllLinesAsync(TestPaths.Fixture(MalformedFixture));
        var withoutShortRow = lines.Where((_, index) => index != ShortRowLineNumber - 1);

        using var temporary = new TemporaryFile(string.Join('\n', withoutShortRow));
        var parser = FlatFileParser.ForComplaints();

        var exception = await Assert.ThrowsAsync<FlatFileFormatException>(() => ReadAllAsync(parser, temporary.Path));

        Assert.Equal(LongRowLineNumberWithoutShortRow, exception.LineNumber);
        Assert.Equal(ComplaintFields.FieldCount, exception.ExpectedFieldCount);
        Assert.Equal(52, exception.ActualFieldCount);
    }

    [Fact]
    public async Task BlankLinesAreSkippedAndCountedInsteadOfThrowing()
    {
        var parser = FlatFileParser.ForRecalls();
        var records = await ReadAllAsync(parser, TestPaths.Fixture(RecallsFixture));

        Assert.Equal(10, records.Count);
        Assert.Equal(10L, parser.LinesRead);
        Assert.Equal(1L, parser.BlankLinesSkipped);
    }

    [Fact]
    public async Task CarriageReturnNeverLeaksIntoTheLastFieldOfACrlfFile()
    {
        var rows = await ReadAllAsync(FlatFileParser.ForRecalls(), TestPaths.Fixture(RecallsFixture));
        var crlfContent = string.Join("\r\n", rows.Select(row => string.Join('\t', row))) + "\r\n";

        using var temporary = new TemporaryFile(crlfContent);
        var parser = FlatFileParser.ForRecalls();
        var records = await ReadAllAsync(parser, temporary.Path);

        Assert.Equal(10, records.Count);
        Assert.All(records, record => Assert.DoesNotContain("\r", record[RecallFields.ParkOutside], StringComparison.Ordinal));
        Assert.All(records, record => Assert.DoesNotContain("\r", record[RecallFields.DoNotDrive], StringComparison.Ordinal));
        Assert.Contains(records, record => record[RecallFields.ParkOutside] == "Yes");
        Assert.Contains(records, record => record[RecallFields.DoNotDrive] == "Yes");
    }

    [Fact]
    public async Task LastFieldOfTheCommittedRecallsFixtureCarriesNoCarriageReturn()
    {
        var records = await ReadAllAsync(FlatFileParser.ForRecalls(), TestPaths.Fixture(RecallsFixture));

        Assert.All(records, record => Assert.DoesNotContain("\r", record[RecallFields.ParkOutside], StringComparison.Ordinal));
        Assert.All(records, record => Assert.True(record[RecallFields.ParkOutside] is "Yes" or "No"));
    }

    [Fact]
    public async Task ProgressIsReportedEveryHundredThousandLines()
    {
        var recorder = new ProgressRecorder();
        var parser = FlatFileParser.ForComplaints();

        await ReadAllAsync(parser, generatedFile.Path, recorder);

        Assert.Equal(GeneratedFlatFile.LineCount, parser.LinesRead);
        Assert.Equal(2, recorder.Reports.Count);
        Assert.Equal(100_000L, recorder.Reports[0]);
        Assert.Equal(200_000L, recorder.Reports[1]);
    }

    [Fact]
    public async Task CancellationStopsEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        var parser = FlatFileParser.ForComplaints();
        var consumed = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var record in parser.ReadRecordsAsync(TestPaths.Fixture(ValidFixture), cancellationToken: cancellation.Token))
            {
                Assert.NotEmpty(record);
                consumed++;

                if (consumed == 5)
                {
                    await cancellation.CancelAsync();
                }
            }
        });

        Assert.Equal(5, consumed);
    }

    [Fact]
    public async Task BreakingOutOfTheEnumerationStopsAfterTheConsumedRecords()
    {
        var parser = FlatFileParser.ForComplaints();
        var consumed = 0;

        await foreach (var record in parser.ReadRecordsAsync(TestPaths.Fixture(ValidFixture)))
        {
            Assert.Equal(ComplaintFields.FieldCount, record.Length);

            if (++consumed == 3)
            {
                break;
            }
        }

        Assert.Equal(3L, parser.LinesRead);
    }

    [Fact]
    public async Task LargeFileIsNeverReadPastTheConsumedRecords()
    {
        var parser = FlatFileParser.ForComplaints();
        var consumed = 0;

        await foreach (var record in parser.ReadRecordsAsync(generatedFile.Path))
        {
            Assert.Equal(ComplaintFields.FieldCount, record.Length);

            if (++consumed == 3)
            {
                break;
            }
        }

        Assert.Equal(3L, parser.LinesRead);
        Assert.Equal(0L, parser.BlankLinesSkipped);
    }

    [Fact]
    public async Task ParserStateIsResetBetweenRuns()
    {
        var parser = FlatFileParser.ForComplaints();

        await ReadAllAsync(parser, TestPaths.Fixture(ValidFixture));
        await ReadAllAsync(parser, TestPaths.Fixture(ValidFixture));

        Assert.Equal(30L, parser.LinesRead);
    }

    [Fact]
    public async Task ByteOrderMarkNeverLeaksIntoTheFirstField()
    {
        using var temporary = new TemporaryFile(ComplaintLine("1000001") + "\n", emitByteOrderMark: true);

        var records = await ReadAllAsync(FlatFileParser.ForComplaints(), temporary.Path);

        Assert.Equal("1000001", records[0][ComplaintFields.ComplaintId]);
    }

    [Fact]
    public async Task MultiByteUtf8CharactersSurviveParsing()
    {
        const string narrative = "MOTEUR ÉLECTRIQUE — DÉFAUT DE FABRICATION, ÜBERHITZUNG, 温度異常";
        var fields = ComplaintLine("1000002").Split('\t');
        fields[ComplaintFields.ComplaintDescription] = narrative;

        using var temporary = new TemporaryFile(string.Join('\t', fields) + "\n");

        var records = await ReadAllAsync(FlatFileParser.ForComplaints(), temporary.Path);

        Assert.Equal(narrative, records[0][ComplaintFields.ComplaintDescription]);
    }

    private static string ComplaintLine(string complaintId) =>
        string.Join('\t', Enumerable.Range(0, ComplaintFields.FieldCount).Select(index => index == 0 ? complaintId : $"f{index}"));

    private static async Task<List<string[]>> ReadAllAsync(FlatFileParser parser, string path, IProgress<long>? progress = null)
    {
        var records = new List<string[]>();

        await foreach (var record in parser.ReadRecordsAsync(path, progress))
        {
            records.Add(record);
        }

        return records;
    }

    private sealed class ProgressRecorder : IProgress<long>
    {
        private readonly List<long> reports = [];

        public IReadOnlyList<long> Reports => reports;

        public void Report(long value) => reports.Add(value);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string content, bool emitByteOrderMark = false)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"garage-doctor-{Guid.NewGuid():N}.tsv");
            File.WriteAllText(Path, content, new UTF8Encoding(emitByteOrderMark));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
