namespace Trackii.App.Services;

public sealed class AppSession
{
    public bool IsLoggedIn { get; private set; }
    public uint UserId { get; private set; }
    public uint DeviceId { get; private set; }
    public uint LocationId { get; private set; }
    public string Username { get; private set; } = "Sin usuario";
    public string DeviceName { get; private set; } = "Dispositivo";
    public string LocationName { get; private set; } = "Sin localidad";

    public void SetLoggedIn(uint userId, string username, uint deviceId, string? deviceName, uint locationId, string? locationName)
    {
        IsLoggedIn = true;
        UserId = userId;
        DeviceId = deviceId;
        LocationId = locationId;
        if (!string.IsNullOrWhiteSpace(username))
        {
            Username = username;
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            DeviceName = deviceName;
        }

        if (!string.IsNullOrWhiteSpace(locationName))
        {
            LocationName = locationName;
        }
    }

    public void Clear()
    {
        IsLoggedIn = false;
        UserId = 0;
        DeviceId = 0;
        LocationId = 0;
        Username = "Sin usuario";
        DeviceName = "Dispositivo";
        LocationName = "Sin localidad";
    }
}
