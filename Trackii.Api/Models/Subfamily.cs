namespace Trackii.Api.Models;

public sealed class Subfamily
{
    public uint Id { get; set; }
    public uint FamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public uint? ActiveRouteId { get; set; }

    public Family? Family { get; set; }
    public Route? ActiveRoute { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
