namespace Trackii.App.Models;

public sealed class LocationDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
