namespace Trackii.Api.Models;

public sealed class Role
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}
