namespace Trackii.Api.Models;

public sealed class Product
{
    public uint Id { get; set; }
    public uint SubfamilyId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public bool Active { get; set; }

    public Subfamily? Subfamily { get; set; }
    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}
