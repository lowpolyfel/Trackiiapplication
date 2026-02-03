namespace Trackii.Api.Models;

public sealed class Area
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }

    public ICollection<Family> Families { get; set; } = new List<Family>();
}
