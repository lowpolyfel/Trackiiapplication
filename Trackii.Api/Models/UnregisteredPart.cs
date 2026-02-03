namespace Trackii.Api.Models;

public sealed class UnregisteredPart
{
    public uint PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public DateTime CreationDateTime { get; set; }
    public bool Active { get; set; }
}
