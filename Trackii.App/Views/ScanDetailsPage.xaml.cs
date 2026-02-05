using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Trackii.App.Models;
using Trackii.App.Services;

namespace Trackii.App.Views;

[QueryProperty(nameof(Order), "order")]
[QueryProperty(nameof(Part), "part")]
public partial class ScanDetailsPage : ContentPage
{
    private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
    private readonly AppSession _session;
    private readonly ApiClient _apiClient;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private bool _isProcessing;
    private uint? _maxQuantity;
    private PartLookupResponse? _partInfo;
    private WorkOrderContextResponse? _workOrderContext;
    private int _logoTapCount;

    private string _order = string.Empty;
    private string _part = string.Empty;

    public string Order
    {
        get => _order;
        set
        {
            _order = Uri.UnescapeDataString(value ?? string.Empty);
            if (OrderEntry is not null)
            {
                OrderEntry.Text = _order;
            }
        }
    }

    public string Part
    {
        get => _part;
        set
        {
            _part = Uri.UnescapeDataString(value ?? string.Empty);
            if (PartEntry is not null)
            {
                PartEntry.Text = _part;
            }
        }
    }

    public ScanDetailsPage()
    {
        InitializeComponent();
        _session = App.Services.GetRequiredService<AppSession>();
        _apiClient = App.Services.GetRequiredService<ApiClient>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateHeader();

        if (!string.IsNullOrWhiteSpace(Order))
        {
            OrderEntry.Text = Order;
        }

        if (!string.IsNullOrWhiteSpace(Part))
        {
            PartEntry.Text = Part;
        }

        await TryLoadContextAsync();
    }

    private void UpdateHeader()
    {
        HeaderLocationLabel.Text = $"Localidad: {_session.LocationName}";
        HeaderDeviceLabel.Text = $"Tableta: {_session.DeviceName}";

        if (!_session.IsLoggedIn)
        {
            HeaderLocationLabel.Text = "Localidad: sin sesión";
            HeaderDeviceLabel.Text = "Tableta: sin sesión";
        }
    }

    private async void OnLogoTapped(object? sender, TappedEventArgs e)
    {
        _logoTapCount++;
        if (_logoTapCount < 5)
        {
            return;
        }

        _logoTapCount = 0;
        await Shell.Current.GoToAsync("//Login");
    }

    private async void OnOrderChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            return;
        }

        if (!OrderRegex.IsMatch(e.NewTextValue))
        {
            QuantityHintLabel.Text = "La orden debe tener 7 dígitos.";
            return;
        }

        await TryLoadContextAsync();
    }

    private async void OnPartChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            return;
        }

        await TryLoadContextAsync();
    }

    private async Task TryLoadContextAsync()
    {
        var order = OrderEntry.Text?.Trim();
        var part = PartEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(order) || string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        if (!OrderRegex.IsMatch(order))
        {
            return;
        }

        if (!await _scanLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            await SetLoadingStateAsync(true);
            await LoadWorkOrderContextAsync(order);
            await LoadPartInfoAsync(part);
        }
        finally
        {
            _isProcessing = false;
            await SetLoadingStateAsync(false);
            _scanLock.Release();
        }
    }

    private async Task LoadPartInfoAsync(string partNumber)
    {
        try
        {
            var response = await _apiClient.GetPartInfoAsync(partNumber, CancellationToken.None);
            _partInfo = response;
            if (!response.Found)
            {
                AreaEntry.Text = "-";
                FamilyEntry.Text = "-";
                SubfamilyEntry.Text = "-";
                StatusLabel.Text = response.Message ?? "Producto no registrado.";
                return;
            }

            AreaEntry.Text = response.AreaName ?? "-";
            FamilyEntry.Text = response.FamilyName ?? "-";
            SubfamilyEntry.Text = response.SubfamilyName ?? "-";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error al consultar producto: {ex.Message}";
        }
    }

    private async Task LoadWorkOrderContextAsync(string workOrderNumber)
    {
        if (!_session.IsLoggedIn)
        {
            StatusLabel.Text = "Inicia sesión para continuar.";
            return;
        }

        try
        {
            var response = await _apiClient.GetWorkOrderContextAsync(workOrderNumber, _session.DeviceId, CancellationToken.None);
            _workOrderContext = response;
            if (!response.Found)
            {
                _maxQuantity = null;
                QuantityEntry.Text = string.Empty;
                QuantityHintLabel.Text = _session.LocationId == 1
                    ? "Orden no encontrada. Alloy puede crearla al registrar."
                    : response.Message ?? "Orden no encontrada.";
                return;
            }

            if (!response.CanProceed)
            {
                _maxQuantity = null;
                QuantityEntry.Text = string.Empty;
                QuantityHintLabel.Text = response.Message ?? "No se puede avanzar.";
                return;
            }

            _maxQuantity = response.MaxQty;
            if (response.IsFirstStep)
            {
                QuantityEntry.Text = string.Empty;
                QuantityHintLabel.Text = "Primera etapa: ingresa la cantidad.";
            }
            else
            {
                QuantityEntry.Text = response.PreviousQty?.ToString() ?? string.Empty;
                QuantityHintLabel.Text = response.MaxQty is null
                    ? "Cantidad previa no disponible."
                    : $"Cantidad máxima: {response.MaxQty}";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error al consultar orden: {ex.Message}";
        }
    }

    private Task SetLoadingStateAsync(bool isLoading)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            LoadingOverlay.IsVisible = isLoading;
            LoadingIndicator.IsRunning = isLoading;
        });
    }

    private void OnQuantityChanged(object? sender, TextChangedEventArgs e)
    {
        if (_maxQuantity is null || string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            return;
        }

        if (uint.TryParse(e.NewTextValue, out var value) && value > _maxQuantity.Value)
        {
            QuantityEntry.Text = _maxQuantity.Value.ToString();
            QuantityHintLabel.Text = $"Cantidad máxima: {_maxQuantity}";
        }
    }

    private async void OnRegisterClicked(object? sender, EventArgs e)
    {
        if (!_session.IsLoggedIn)
        {
            StatusLabel.Text = "Inicia sesión para registrar.";
            return;
        }

        if (_partInfo is null || !_partInfo.Found)
        {
            StatusLabel.Text = "Producto no válido.";
            return;
        }

        if (_workOrderContext is null)
        {
            StatusLabel.Text = "Orden no válida.";
            return;
        }

        if (!_workOrderContext.Found && _session.LocationId != 1)
        {
            StatusLabel.Text = _workOrderContext.Message ?? "Orden no encontrada.";
            return;
        }

        if (_workOrderContext.Found && !_workOrderContext.CanProceed)
        {
            StatusLabel.Text = _workOrderContext.Message ?? "Orden no válida.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OrderEntry.Text) || string.IsNullOrWhiteSpace(PartEntry.Text))
        {
            StatusLabel.Text = "Captura orden y parte.";
            return;
        }

        if (!uint.TryParse(QuantityEntry.Text, out var quantity) || quantity == 0)
        {
            StatusLabel.Text = "Cantidad inválida.";
            return;
        }

        if (_maxQuantity is not null && quantity > _maxQuantity.Value)
        {
            StatusLabel.Text = "Cantidad mayor a la permitida.";
            return;
        }

        try
        {
            var response = await _apiClient.RegisterScanAsync(new RegisterScanRequest
            {
                WorkOrderNumber = OrderEntry.Text.Trim(),
                PartNumber = PartEntry.Text.Trim(),
                Quantity = quantity,
                UserId = _session.UserId,
                DeviceId = _session.DeviceId
            }, CancellationToken.None);
            StatusLabel.Text = response.Message;
            DetectionLabel.Text = "Registro exitoso.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"No se pudo registrar: {ex.Message}";
        }
    }

    private async void OnScrapClicked(object? sender, EventArgs e)
    {
        if (!_session.IsLoggedIn)
        {
            StatusLabel.Text = "Inicia sesión para cancelar.";
            return;
        }

        if (string.IsNullOrWhiteSpace(OrderEntry.Text))
        {
            StatusLabel.Text = "Captura la orden.";
            return;
        }

        try
        {
            var orderNumber = OrderEntry.Text.Trim();
            await LoadWorkOrderContextAsync(orderNumber);

            if (_workOrderContext is null || !_workOrderContext.Found)
            {
                StatusLabel.Text = _workOrderContext?.Message ?? "Orden no encontrada.";
                return;
            }

            var status = _workOrderContext.WorkOrderStatus?.Trim().ToUpperInvariant();
            if (status is "CANCELLED" or "FINISHED")
            {
                StatusLabel.Text = "La orden no está activa.";
                return;
            }

            if (!_workOrderContext.CanProceed)
            {
                StatusLabel.Text = _workOrderContext.Message ?? "La orden no está activa.";
                return;
            }

            var response = await _apiClient.ScrapAsync(new ScrapRequest
            {
                WorkOrderNumber = orderNumber,
                UserId = _session.UserId,
                DeviceId = _session.DeviceId,
                Reason = "Scrap desde detalle"
            }, CancellationToken.None);
            StatusLabel.Text = response.Message;
            DetectionLabel.Text = "Orden cancelada.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"No se pudo cancelar: {ex.Message}";
        }
    }

    private async void OnReworkClicked(object? sender, EventArgs e)
    {
        try
        {
            var orderNumber = OrderEntry.Text?.Trim();
            var route = string.IsNullOrWhiteSpace(orderNumber)
                ? nameof(ReworkPage)
                : $"{nameof(ReworkPage)}?order={Uri.EscapeDataString(orderNumber)}";
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"No se pudo abrir rework: {ex.Message}";
        }
    }

    private async void OnBackScannerClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//Scanner");
    }
}
