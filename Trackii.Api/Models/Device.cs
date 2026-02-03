namespace Trackii.Api.Models;

public sealed class Device
{
    public uint Id { get; set; }
    public string DeviceUid { get; set; } = string.Empty;
    public uint LocationId { get; set; }
    public uint? UserId { get; set; }
    public string? Name { get; set; }
    public bool Active { get; set; }

    public Location? Location { get; set; }
    public User? User { get; set; }
}
