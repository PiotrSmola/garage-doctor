namespace GarageDoctor.Domain.Parsing;

public sealed class FlatFileFormatException : Exception
{
    public FlatFileFormatException(string filePath, long lineNumber, int expectedFieldCount, int actualFieldCount)
        : base($"Line {lineNumber} of '{filePath}' has {actualFieldCount} tab-separated fields but {expectedFieldCount} were expected.")
    {
        FilePath = filePath;
        LineNumber = lineNumber;
        ExpectedFieldCount = expectedFieldCount;
        ActualFieldCount = actualFieldCount;
    }

    public long LineNumber { get; }

    public int ExpectedFieldCount { get; }

    public int ActualFieldCount { get; }

    public string FilePath { get; }
}
