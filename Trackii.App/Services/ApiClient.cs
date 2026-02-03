using System.Net.Http.Json;
using Trackii.App.Configuration;
using Trackii.App.Models;

namespace Trackii.App.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(AppConfig.ApiBaseUrl);
        }
    }

    public async Task<IReadOnlyList<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("api/locations", cancellationToken);
        response.EnsureSuccessStatusCode();

        var locations = await response.Content.ReadFromJsonAsync<List<LocationDto>>(cancellationToken: cancellationToken);
        return locations ?? new List<LocationDto>();
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        return payload ?? new LoginResponse();
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/register", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: cancellationToken);
        return payload ?? new RegisterResponse();
    }

    public async Task<PartLookupResponse> GetPartInfoAsync(string partNumber, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"api/scanner/part/{Uri.EscapeDataString(partNumber)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<PartLookupResponse>(cancellationToken: cancellationToken);
        return payload ?? new PartLookupResponse();
    }

    public async Task<WorkOrderContextResponse> GetWorkOrderContextAsync(string workOrderNumber, uint deviceId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"api/scanner/work-orders/{Uri.EscapeDataString(workOrderNumber)}/context?deviceId={deviceId}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<WorkOrderContextResponse>(cancellationToken: cancellationToken);
        return payload ?? new WorkOrderContextResponse();
    }

    public async Task<RegisterScanResponse> RegisterScanAsync(RegisterScanRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/scanner/register", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RegisterScanResponse>(cancellationToken: cancellationToken);
        return payload ?? new RegisterScanResponse();
    }

    public async Task<ScrapResponse> ScrapAsync(ScrapRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/scanner/scrap", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ScrapResponse>(cancellationToken: cancellationToken);
        return payload ?? new ScrapResponse();
    }

    public async Task<ReworkResponse> ReworkAsync(ReworkRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/scanner/rework", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ReworkResponse>(cancellationToken: cancellationToken);
        return payload ?? new ReworkResponse();
    }
}
