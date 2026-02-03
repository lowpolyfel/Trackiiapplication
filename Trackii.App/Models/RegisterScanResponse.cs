namespace Trackii.App.Models;

public sealed class RegisterScanResponse
{
    public string Message { get; set; } = string.Empty;
    public uint WorkOrderId { get; set; }
    public uint WipItemId { get; set; }
    public uint RouteStepId { get; set; }
    public bool IsFinalStep { get; set; }
}
