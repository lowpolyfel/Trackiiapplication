namespace Trackii.App.Models;

public sealed class LoginResponse
{
    public uint UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public uint RoleId { get; set; }
}
