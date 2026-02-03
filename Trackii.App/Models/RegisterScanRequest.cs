namespace Trackii.App.Models;

public sealed class RegisterScanRequest
{
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public uint Quantity { get; set; }
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
}
