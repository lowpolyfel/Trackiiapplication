namespace Trackii.Api.Models;

public sealed class Location
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
