using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;

namespace Trackii.App;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly AppSession _session;

    public LoginPage()
    {
        InitializeComponent();
        _apiClient = App.Services.GetRequiredService<ApiClient>();
        _session = App.Services.GetRequiredService<AppSession>();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = string.Empty;

        var request = new LoginRequest
        {
            Username = UsernameEntry.Text?.Trim() ?? string.Empty,
            Password = PasswordEntry.Text ?? string.Empty
        };

        try
        {
            var response = await _apiClient.LoginAsync(request, CancellationToken.None);
            _session.SetLoggedIn(response.Username, _session.DeviceName, _session.LocationName);
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
