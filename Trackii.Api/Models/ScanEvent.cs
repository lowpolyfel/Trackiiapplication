namespace Trackii.Api.Models;

public sealed class ScanEvent
{
    public uint Id { get; set; }
    public uint WipItemId { get; set; }
    public uint RouteStepId { get; set; }
    public string ScanType { get; set; } = "ENTRY";
    public DateTime Ts { get; set; }

    public WipItem? WipItem { get; set; }
    public RouteStep? RouteStep { get; set; }
}
