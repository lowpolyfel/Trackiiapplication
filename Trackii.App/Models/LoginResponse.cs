namespace Trackii.App.Models;

public sealed class LoginResponse
{
    public uint UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public uint RoleId { get; set; }
    public uint DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public uint LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
}
