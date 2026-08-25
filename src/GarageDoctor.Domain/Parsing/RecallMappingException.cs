namespace GarageDoctor.Domain.Parsing;

public sealed class RecallMappingException : Exception
{
    public RecallMappingException(string recordId, string message)
        : base(message) => RecordId = recordId;

    public string RecordId { get; }
}
