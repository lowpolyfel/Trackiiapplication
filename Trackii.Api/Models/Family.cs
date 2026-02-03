namespace Trackii.Api.Models;

public sealed class Family
{
    public uint Id { get; set; }
    public uint AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }

    public Area? Area { get; set; }
    public ICollection<Subfamily> Subfamilies { get; set; } = new List<Subfamily>();
}
