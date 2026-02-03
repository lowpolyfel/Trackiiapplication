namespace Trackii.Api.Contracts;

public sealed record ScrapResponse(
    string Message,
    uint WorkOrderId,
    uint? WipItemId);
