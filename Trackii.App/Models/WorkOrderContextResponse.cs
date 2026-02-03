namespace Trackii.App.Models;

public sealed class WorkOrderContextResponse
{
    public bool Found { get; set; }
    public string? Message { get; set; }
    public uint? WorkOrderId { get; set; }
    public string? WorkOrderStatus { get; set; }
    public uint? ProductId { get; set; }
    public string? PartNumber { get; set; }
    public uint? RouteId { get; set; }
    public uint? CurrentStepId { get; set; }
    public uint? NextStepId { get; set; }
    public uint? NextStepNumber { get; set; }
    public uint? NextStepLocationId { get; set; }
    public string? NextStepLocationName { get; set; }
    public uint? PreviousQty { get; set; }
    public uint? MaxQty { get; set; }
    public bool IsFirstStep { get; set; }
    public bool CanProceed { get; set; }
}
