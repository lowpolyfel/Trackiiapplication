using Android.Provider;
using Trackii.App.Services;

namespace Trackii.App.Platforms.Android;

public sealed class DeviceIdService : IDeviceIdService
{
    public Task<string> GetDeviceIdAsync()
    {
        var id = Settings.Secure.GetString(global::Android.App.Application.Context.ContentResolver, Settings.Secure.AndroidId);
        return Task.FromResult(id ?? string.Empty);
    }
}
