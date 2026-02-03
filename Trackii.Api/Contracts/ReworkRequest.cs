namespace Trackii.Api.Contracts;

public sealed record ReworkRequest(
    string WorkOrderNumber,
    uint Quantity,
    uint UserId,
    uint DeviceId,
    string? Reason,
    bool Completed);
