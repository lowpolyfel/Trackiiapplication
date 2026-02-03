namespace Trackii.App.Models;

public sealed class ScrapResponse
{
    public string Message { get; set; } = string.Empty;
    public uint WorkOrderId { get; set; }
    public uint? WipItemId { get; set; }
}
