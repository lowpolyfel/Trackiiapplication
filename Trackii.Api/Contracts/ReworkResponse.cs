namespace Trackii.Api.Contracts;

public sealed record ReworkResponse(
    string Message,
    uint WorkOrderId,
    uint WipItemId,
    string WipStatus);
