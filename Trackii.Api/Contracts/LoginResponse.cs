namespace Trackii.Api.Contracts;

public sealed record LoginResponse(
    uint UserId,
    string Username,
    uint RoleId,
    uint DeviceId,
    string DeviceName,
    uint LocationId,
    string LocationName);
