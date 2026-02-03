using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Trackii.App
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(25);
        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private readonly AppSession _session;
        private readonly ApiClient _apiClient;
        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private bool _isCapturing;
        private bool _isProcessing;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;
        private uint? _maxQuantity;
        private PartLookupResponse? _partInfo;
        private WorkOrderContextResponse? _workOrderContext;

        public ScannerPage()
        {
            InitializeComponent();
            _session = App.Services.GetRequiredService<AppSession>();
            _apiClient = App.Services.GetRequiredService<ApiClient>();
            BuildScanner();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            UpdateHeader();
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            _hasPermission = status == PermissionStatus.Granted;
            if (!_hasPermission)
            {
                StatusLabel.Text = "Permiso de cámara requerido.";
                StopScanner();
                return;
            }

            UpdateHeader();
            StatusLabel.Text = "Escaneando automáticamente...";
            DetectionLabel.Text = "Esperando código...";
            StartScanner();
            StartScanAnimation();
        }

        protected override void OnDisappearing()
        {
            StopScanner();
            StopScanAnimation();
            CancelDetectedOverlay();
            base.OnDisappearing();
        }

        private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            if (!_isCapturing)
            {
                return;
            }

            var result = e.Results?.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            try
            {
                _lastDetectionAt = DateTime.UtcNow;
                var now = _lastDetectionAt;
                if (result == _lastResult && now - _lastScanAt < ScanCooldown)
                {
                    return;
                }

                _lastResult = result;
                _lastScanAt = now;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    StatusLabel.Text = $"Leído: {result}";
                    DetectionLabel.Text = "Detectado al instante.";
                    await ShowDetectedAsync(result);
                    if (OrderRegex.IsMatch(result))
                    {
                        OrderEntry.Text = result;
                    }
                    else
                    {
                        PartEntry.Text = result;
                    }

                    await TryFinalizeScanAsync();
                });
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al procesar lectura: {ex.Message}";
            }
        }

        private void OnTorchClicked(object? sender, EventArgs e)
        {
            if (_barcodeReader is null)
            {
                StatusLabel.Text = "Cámara no disponible.";
                return;
            }

            _barcodeReader.IsTorchOn = !_barcodeReader.IsTorchOn;
            StatusLabel.Text = _barcodeReader.IsTorchOn ? "Linterna encendida" : "Linterna apagada";
        }

        private void OnRefocusClicked(object? sender, EventArgs e)
        {
            if (_barcodeReader is null)
            {
                StatusLabel.Text = "Cámara no disponible.";
                return;
            }

            StatusLabel.Text = "Reenfocando...";
            _barcodeReader.IsDetecting = true;
            _isCapturing = true;
            CaptureToggleButton.Text = "Pausar";
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            try
            {
                StopScanner();
                StopScanAnimation();
                CancelDetectedOverlay();
                await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//Login"));
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al abrir login: {ex.Message}";
            }
        }

        private void OnCaptureToggleClicked(object? sender, EventArgs e)
        {
            if (_barcodeReader is null)
            {
                StatusLabel.Text = "Cámara no disponible.";
                return;
            }

            _isCapturing = !_isCapturing;
            _barcodeReader.IsDetecting = _isCapturing;
            CaptureToggleButton.Text = _isCapturing ? "Pausar" : "Iniciar";
            StatusLabel.Text = _isCapturing ? "Escaneando automáticamente..." : "Captura pausada";
            DetectionLabel.Text = _isCapturing ? "Esperando código..." : "Pulsa iniciar para escanear";
        }

        private void BuildScanner()
        {
            var reader = new CameraBarcodeReaderView
            {
                CameraLocation = CameraLocation.Rear,
                IsDetecting = true,
                Options = new BarcodeReaderOptions
                {
                    AutoRotate = true,
                    TryHarder = false,
                    TryInverted = true,
                    Multiple = false,
                }
            };

            reader.BarcodesDetected += OnBarcodesDetected;
            ScannerHost.Content = reader;
            _barcodeReader = reader;
        }

        private void DisposeScanner()
        {
            if (_barcodeReader is not null)
            {
                _barcodeReader.BarcodesDetected -= OnBarcodesDetected;
                _barcodeReader.IsDetecting = false;
            }

            ScannerHost.Content = null;
            _barcodeReader = null;
        }

        private void StartScanner()
        {
            if (!_hasPermission)
            {
                return;
            }

            StopScanner();
            BuildScanner();

            if (_barcodeReader is not null)
            {
                _barcodeReader.IsDetecting = true;
            }

            _isCapturing = true;
            CaptureToggleButton.Text = "Pausar";
        }

        private void StopScanner()
        {
            DisposeScanner();
            CancelDetectedOverlay();
            _isCapturing = false;
            if (CaptureToggleButton is not null)
            {
                CaptureToggleButton.Text = "Iniciar";
            }
        }

        private void UpdateHeader()
        {
            if (_session.IsLoggedIn)
            {
                AuthTitleLabel.Text = _session.DeviceName;
                AuthSubtitleLabel.Text = $"Cuenta: {_session.Username} • Localidad: {_session.LocationName}";
                AuthCard.BackgroundColor = Color.FromArgb("#E2E8F0");
                LoginButton.IsVisible = false;
            }
            else
            {
                AuthTitleLabel.Text = "Inicia sesión";
                AuthSubtitleLabel.Text = "Logeate acá para continuar.";
                AuthCard.BackgroundColor = Color.FromArgb("#F1F5F9");
                LoginButton.IsVisible = true;
            }
        }

        private async void OnOrderChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            if (OrderRegex.IsMatch(e.NewTextValue))
            {
                await TryFinalizeScanAsync();
            }
        }

        private async void OnPartChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            await TryFinalizeScanAsync();
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
                    await DisplayAlert("Producto no encontrado", response.Message ?? "Producto no registrado.", "OK");
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

        private async Task TryFinalizeScanAsync()
        {
            var order = OrderEntry.Text?.Trim();
            var part = PartEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(order) || string.IsNullOrWhiteSpace(part))
            {
                return;
            }

            if (!await _processingLock.WaitAsync(0))
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
                _processingLock.Release();
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
                ResetForm();
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
                var response = await _apiClient.ScrapAsync(new ScrapRequest
                {
                    WorkOrderNumber = OrderEntry.Text.Trim(),
                    UserId = _session.UserId,
                    DeviceId = _session.DeviceId,
                    Reason = "Scrap desde scanner"
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
            if (!_session.IsLoggedIn)
            {
                StatusLabel.Text = "Inicia sesión para rework.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OrderEntry.Text))
            {
                StatusLabel.Text = "Captura la orden.";
                return;
            }

            if (!uint.TryParse(QuantityEntry.Text, out var quantity) || quantity == 0)
            {
                StatusLabel.Text = "Cantidad inválida para rework.";
                return;
            }

            var reason = await DisplayPromptAsync("Rework", "Motivo de rework (opcional):");
            var completed = await DisplayAlert("Rework", "¿Terminó el rework?", "Sí", "No");

            try
            {
                var response = await _apiClient.ReworkAsync(new ReworkRequest
                {
                    WorkOrderNumber = OrderEntry.Text.Trim(),
                    Quantity = quantity,
                    UserId = _session.UserId,
                    DeviceId = _session.DeviceId,
                    Reason = reason,
                    Completed = completed
                }, CancellationToken.None);
                StatusLabel.Text = response.Message;
                DetectionLabel.Text = $"Estado WIP: {response.WipStatus}";
                await LoadWorkOrderContextAsync(OrderEntry.Text.Trim());
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"No se pudo registrar rework: {ex.Message}";
            }
        }

        private void OnPendingClicked(object? sender, EventArgs e)
        {
            _isProcessing = false;
            _ = SetLoadingStateAsync(false);
            CancelDetectedOverlay();
            ResetForm();
            StatusLabel.Text = "Registro marcado como pendiente.";
            DetectionLabel.Text = "Campos reiniciados.";
        }

        private void StartScanAnimation()
        {
            _animationCts?.Cancel();
            _animationCts = new CancellationTokenSource();
            _ = AnimateScanLineAsync(_animationCts.Token);
        }

        private void StopScanAnimation()
        {
            _animationCts?.Cancel();
            _animationCts = null;
        }

        private async Task AnimateScanLineAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var travel = ScannerHost.Height;
                    if (travel <= 0)
                    {
                        await Task.Delay(200, token);
                        continue;
                    }

                    await ScanLine.TranslateTo(0, travel, 1200, Easing.CubicInOut);
                    await ScanLine.TranslateTo(0, 0, 1200, Easing.CubicInOut);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private async Task ShowDetectedAsync(string result)
        {
            CancelDetectedOverlay();
            _detectedCts = new CancellationTokenSource();
            var token = _detectedCts.Token;
            try
            {
                DetectedTextLabel.Text = $"Detectado: {result}";
                DetectedOverlay.Opacity = 0;
                DetectedOverlay.Scale = 0.9;
                await Task.WhenAll(
                    DetectedOverlay.FadeTo(1, 120, Easing.CubicOut),
                    DetectedOverlay.ScaleTo(1, 120, Easing.CubicOut));
                await Task.Delay(350, token);
                await DetectedOverlay.FadeTo(0, 400, Easing.CubicIn);
            }
            catch (TaskCanceledException)
            {
                // Ignore cancelled animation
            }
            catch (ObjectDisposedException)
            {
                // Ignore disposed views
            }
        }

        private void CancelDetectedOverlay()
        {
            _detectedCts?.Cancel();
            _detectedCts = null;
        }

        private void ResetForm()
        {
            OrderEntry.Text = string.Empty;
            PartEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            QuantityHintLabel.Text = string.Empty;
            AreaEntry.Text = "-";
            FamilyEntry.Text = "-";
            SubfamilyEntry.Text = "-";
            _partInfo = null;
            _workOrderContext = null;
            _maxQuantity = null;
        }
    }
}
