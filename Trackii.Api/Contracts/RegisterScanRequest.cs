namespace Trackii.Api.Contracts;

public sealed record RegisterScanRequest(
    string WorkOrderNumber,
    string PartNumber,
    uint Quantity,
    uint UserId,
    uint DeviceId);
