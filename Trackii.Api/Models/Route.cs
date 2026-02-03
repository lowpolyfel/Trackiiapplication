namespace Trackii.Api.Models;

public sealed class Route
{
    public uint Id { get; set; }
    public uint SubfamilyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Active { get; set; }

    public Subfamily? Subfamily { get; set; }
    public ICollection<RouteStep> Steps { get; set; } = new List<RouteStep>();
}
