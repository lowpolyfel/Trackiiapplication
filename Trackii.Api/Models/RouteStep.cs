namespace Trackii.Api.Models;

public sealed class RouteStep
{
    public uint Id { get; set; }
    public uint RouteId { get; set; }
    public uint StepNumber { get; set; }
    public uint LocationId { get; set; }

    public Route? Route { get; set; }
    public Location? Location { get; set; }
    public ICollection<WipStepExecution> StepExecutions { get; set; } = new List<WipStepExecution>();
}
