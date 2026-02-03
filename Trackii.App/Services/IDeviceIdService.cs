namespace Trackii.App.Services;

public interface IDeviceIdService
{
    Task<string> GetDeviceIdAsync();
}
