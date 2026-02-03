using Microsoft.Maui.Storage;

namespace Trackii.App.Services;

public sealed class AppSession
{
    private const string LoggedInKey = "trackii.session.logged_in";
    private const string UserIdKey = "trackii.session.user_id";
    private const string DeviceIdKey = "trackii.session.device_id";
    private const string LocationIdKey = "trackii.session.location_id";
    private const string UsernameKey = "trackii.session.username";
    private const string DeviceNameKey = "trackii.session.device_name";
    private const string LocationNameKey = "trackii.session.location_name";

    public bool IsLoggedIn { get; private set; }
    public uint UserId { get; private set; }
    public uint DeviceId { get; private set; }
    public uint LocationId { get; private set; }
    public string Username { get; private set; } = "Sin usuario";
    public string DeviceName { get; private set; } = "Dispositivo";
    public string LocationName { get; private set; } = "Sin localidad";

    public AppSession()
    {
        LoadFromPreferences();
    }

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

        SaveToPreferences();
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

        ClearPreferences();
    }

    private void LoadFromPreferences()
    {
        if (!Preferences.Get(LoggedInKey, false))
        {
            return;
        }

        IsLoggedIn = true;
        UserId = (uint)Preferences.Get(UserIdKey, 0);
        DeviceId = (uint)Preferences.Get(DeviceIdKey, 0);
        LocationId = (uint)Preferences.Get(LocationIdKey, 0);
        Username = Preferences.Get(UsernameKey, Username);
        DeviceName = Preferences.Get(DeviceNameKey, DeviceName);
        LocationName = Preferences.Get(LocationNameKey, LocationName);
    }

    private void SaveToPreferences()
    {
        Preferences.Set(LoggedInKey, IsLoggedIn);
        Preferences.Set(UserIdKey, (int)UserId);
        Preferences.Set(DeviceIdKey, (int)DeviceId);
        Preferences.Set(LocationIdKey, (int)LocationId);
        Preferences.Set(UsernameKey, Username);
        Preferences.Set(DeviceNameKey, DeviceName);
        Preferences.Set(LocationNameKey, LocationName);
    }

    private void ClearPreferences()
    {
        Preferences.Remove(LoggedInKey);
        Preferences.Remove(UserIdKey);
        Preferences.Remove(DeviceIdKey);
        Preferences.Remove(LocationIdKey);
        Preferences.Remove(UsernameKey);
        Preferences.Remove(DeviceNameKey);
        Preferences.Remove(LocationNameKey);
    }
}
