namespace Trackii.App.Services;

public sealed class DefaultDeviceIdService : IDeviceIdService
{
    public Task<string> GetDeviceIdAsync()
    {
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }
}
