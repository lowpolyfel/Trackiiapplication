namespace Trackii.Api.Models;

public sealed class WipItem
{
    public uint Id { get; set; }
    public uint WorkOrderId { get; set; }
    public uint CurrentStepId { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime CreatedAt { get; set; }
    public uint RouteId { get; set; }

    public WorkOrder? WorkOrder { get; set; }
    public Route? Route { get; set; }
    public RouteStep? CurrentStep { get; set; }
    public ICollection<WipStepExecution> StepExecutions { get; set; } = new List<WipStepExecution>();
    public ICollection<WipReworkLog> ReworkLogs { get; set; } = new List<WipReworkLog>();
    public ICollection<ScanEvent> ScanEvents { get; set; } = new List<ScanEvent>();
}
