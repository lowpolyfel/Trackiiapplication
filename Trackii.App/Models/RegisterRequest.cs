namespace Trackii.App.Models;

public sealed class RegisterRequest
{
    public string TokenCode { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public uint LocationId { get; set; }
    public string DeviceUid { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
}
