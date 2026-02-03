namespace Trackii.App.Models;

public sealed class PartLookupResponse
{
    public bool Found { get; set; }
    public string? Message { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public uint? ProductId { get; set; }
    public uint? SubfamilyId { get; set; }
    public string? SubfamilyName { get; set; }
    public uint? FamilyId { get; set; }
    public string? FamilyName { get; set; }
    public uint? AreaId { get; set; }
    public string? AreaName { get; set; }
    public uint? ActiveRouteId { get; set; }
}
