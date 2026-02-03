namespace Trackii.App.Models;

public sealed class ScrapRequest
{
    public string WorkOrderNumber { get; set; } = string.Empty;
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
    public string? Reason { get; set; }
}
