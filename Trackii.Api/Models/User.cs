namespace Trackii.Api.Models;

public sealed class User
{
    public uint Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public uint RoleId { get; set; }
    public bool Active { get; set; }

    public Role? Role { get; set; }
}
