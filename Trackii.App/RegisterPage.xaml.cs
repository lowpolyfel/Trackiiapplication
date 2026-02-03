using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;

namespace Trackii.App;

public partial class RegisterPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly IDeviceIdService _deviceIdService;
    private readonly AppSession _session;
    private IReadOnlyList<LocationDto> _locations = Array.Empty<LocationDto>();

    public RegisterPage()
    {
        InitializeComponent();
        _apiClient = App.Services.GetRequiredService<ApiClient>();
        _deviceIdService = App.Services.GetRequiredService<IDeviceIdService>();
        _session = App.Services.GetRequiredService<AppSession>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadLocationsAsync();
    }

    private async Task LoadLocationsAsync()
    {
        try
        {
            _locations = await _apiClient.GetLocationsAsync(CancellationToken.None);
            LocationPicker.ItemsSource = _locations.ToList();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error al cargar localidades: {ex.Message}";
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = string.Empty;

        if (LocationPicker.SelectedItem is not LocationDto location)
        {
            StatusLabel.Text = "Selecciona una localidad.";
            return;
        }

        var deviceId = await _deviceIdService.GetDeviceIdAsync();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            StatusLabel.Text = "No se pudo obtener el Android ID.";
            return;
        }

        var request = new RegisterRequest
        {
            TokenCode = TokenEntry.Text?.Trim() ?? string.Empty,
            Username = UsernameEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty,
            LocationId = location.Id,
            DeviceUid = deviceId,
            DeviceName = DeviceNameEntry.Text?.Trim()
        };

        try
        {
            var response = await _apiClient.RegisterAsync(request, CancellationToken.None);
            var deviceName = string.IsNullOrWhiteSpace(request.DeviceName)
                ? request.Username
                : request.DeviceName;
            _session.SetLoggedIn(response.UserId, request.Username, response.DeviceId, deviceName, location.Id, location.Name);
            await DisplayAlert("Registro", $"Registro completado. Usuario {response.UserId}", "OK");
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
