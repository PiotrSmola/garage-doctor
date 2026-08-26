namespace GarageDoctor.Web.Models;

public sealed record ErrorViewModel
{
    public string? RequestId { get; init; }

    public int StatusCode { get; init; } = StatusCodes.Status500InternalServerError;

    public string? RequestedPath { get; init; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public bool ShowRequestedPath => !string.IsNullOrEmpty(RequestedPath);
}
