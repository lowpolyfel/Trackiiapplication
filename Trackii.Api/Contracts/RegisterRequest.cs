namespace Trackii.Api.Contracts;

public sealed record RegisterRequest(
    string TokenCode,
    string Username,
    string Password,
    uint LocationId,
    string DeviceUid,
    string? DeviceName);
