namespace Trackii.Api.Models;

public sealed class WipStepExecution
{
    public uint Id { get; set; }
    public uint WipItemId { get; set; }
    public uint RouteStepId { get; set; }
    public uint UserId { get; set; }
    public uint DeviceId { get; set; }
    public uint LocationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public uint QtyIn { get; set; }
    public uint QtyScrap { get; set; }

    public WipItem? WipItem { get; set; }
    public RouteStep? RouteStep { get; set; }
    public User? User { get; set; }
    public Device? Device { get; set; }
    public Location? Location { get; set; }
}
