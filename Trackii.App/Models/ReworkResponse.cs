namespace Trackii.App.Models;

public sealed class ReworkResponse
{
    public string Message { get; set; } = string.Empty;
    public uint WorkOrderId { get; set; }
    public uint WipItemId { get; set; }
    public string WipStatus { get; set; } = string.Empty;
}
