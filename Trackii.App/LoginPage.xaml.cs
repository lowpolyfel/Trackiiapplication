using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;

namespace Trackii.App;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly IDeviceIdService _deviceIdService;
    private readonly AppSession _session;

    public LoginPage()
    {
        InitializeComponent();
        _apiClient = App.Services.GetRequiredService<ApiClient>();
        _deviceIdService = App.Services.GetRequiredService<IDeviceIdService>();
        _session = App.Services.GetRequiredService<AppSession>();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = string.Empty;

        var deviceId = await _deviceIdService.GetDeviceIdAsync();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            StatusLabel.Text = "No se pudo obtener el Android ID.";
            return;
        }

        var request = new LoginRequest
        {
            Username = UsernameEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty,
            DeviceUid = deviceId
        };

        try
        {
            var response = await _apiClient.LoginAsync(request, CancellationToken.None);
            _session.SetLoggedIn(response.UserId, response.Username, response.DeviceId, response.DeviceName, response.LocationId, response.LocationName);
            await DisplayAlert("Login", $"Bienvenido {response.Username}", "OK");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async void OnRegisterTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(RegisterPage));
    }
}
