namespace GarageDoctor.Domain.Parsing;

public sealed class ComplaintMappingException : Exception
{
    public ComplaintMappingException(string complaintId, string message)
        : base(message) => ComplaintId = complaintId;

    public string ComplaintId { get; }
}
