namespace Trackii.App.Services;

public sealed class AppSession
{
    public bool IsLoggedIn { get; private set; }
    public string Username { get; private set; } = "Sin usuario";
    public string DeviceName { get; private set; } = "Dispositivo";
    public string LocationName { get; private set; } = "Sin localidad";

    public void SetLoggedIn(string username, string? deviceName, string? locationName)
    {
        IsLoggedIn = true;
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
        Username = "Sin usuario";
        DeviceName = "Dispositivo";
        LocationName = "Sin localidad";
    }
}
