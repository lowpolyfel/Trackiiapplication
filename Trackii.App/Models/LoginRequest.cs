namespace Trackii.App.Models;

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DeviceUid { get; set; } = string.Empty;
}
