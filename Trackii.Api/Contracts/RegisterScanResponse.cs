namespace Trackii.Api.Contracts;

public sealed record RegisterScanResponse(
    string Message,
    uint WorkOrderId,
    uint WipItemId,
    uint RouteStepId,
    bool IsFinalStep);
