namespace Trackii.App.Models;

public sealed class ReworkRequest
{
    public string WorkOrderNumber { get; set; } = string.Empty;
    public uint Quantity { get; set; }
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
    public string? Reason { get; set; }
    public bool Completed { get; set; }
}
