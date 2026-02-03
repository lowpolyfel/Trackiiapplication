namespace Trackii.Api.Contracts;

public sealed record ScrapRequest(
    string WorkOrderNumber,
    uint UserId,
    uint DeviceId,
    string? Reason);
