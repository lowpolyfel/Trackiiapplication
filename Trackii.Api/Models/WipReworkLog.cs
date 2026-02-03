namespace Trackii.Api.Models;

public sealed class WipReworkLog
{
    public uint Id { get; set; }
    public uint WipItemId { get; set; }
    public uint LocationId { get; set; }
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
    public uint Qty { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }

    public WipItem? WipItem { get; set; }
    public Location? Location { get; set; }
    public User? User { get; set; }
    public Device? Device { get; set; }
}
