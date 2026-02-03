namespace Trackii.Api.Contracts;

public sealed record WorkOrderContextResponse(
    bool Found,
    string? Message,
    uint? WorkOrderId,
    string? WorkOrderStatus,
    uint? ProductId,
    string? PartNumber,
    uint? RouteId,
    uint? CurrentStepId,
    uint? NextStepId,
    uint? NextStepNumber,
    uint? NextStepLocationId,
    string? NextStepLocationName,
    uint? PreviousQty,
    uint? MaxQty,
    bool IsFirstStep,
    bool CanProceed);
