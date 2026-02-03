namespace Trackii.Api.Models;

public sealed class WorkOrder
{
    public uint Id { get; set; }
    public string WoNumber { get; set; } = string.Empty;
    public uint ProductId { get; set; }
    public string Status { get; set; } = "OPEN";

    public Product? Product { get; set; }
    public WipItem? WipItem { get; set; }
}
