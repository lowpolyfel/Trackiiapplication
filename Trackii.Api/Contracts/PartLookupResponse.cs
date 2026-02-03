namespace Trackii.Api.Contracts;

public sealed record PartLookupResponse(
    bool Found,
    string? Message,
    string PartNumber,
    uint? ProductId,
    uint? SubfamilyId,
    string? SubfamilyName,
    uint? FamilyId,
    string? FamilyName,
    uint? AreaId,
    string? AreaName,
    uint? ActiveRouteId);
