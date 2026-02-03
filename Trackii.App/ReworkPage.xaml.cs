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
    [QueryProperty(nameof(InitialOrderNumber), "order")]
    public partial class ReworkPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan IdleResetDelay = TimeSpan.FromSeconds(3);
        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private CancellationTokenSource? _idleResetCts;
        private readonly AppSession _session;
        private readonly ApiClient _apiClient;
        private readonly SemaphoreSlim _registerLock = new(1, 1);
        private bool _isProcessing;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;

        public string? InitialOrderNumber { get; set; }

        public ReworkPage()
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
            StatusLabel.Text = "Escaneo rework activo";
            DetectionLabel.Text = "Listo para detectar órdenes.";
            StartScanner();
            StartScanAnimation();
            ScheduleIdleReset();

            if (!string.IsNullOrWhiteSpace(InitialOrderNumber) && string.IsNullOrWhiteSpace(OrderEntry.Text))
            {
                OrderEntry.Text = InitialOrderNumber.Trim();
            }
        }

        protected override void OnDisappearing()
        {
            StopScanner();
            StopScanAnimation();
            CancelDetectedOverlay();
            CancelIdleReset();
            base.OnDisappearing();
        }

        private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            try
            {
                if (_isProcessing)
                {
                    return;
                }

                _lastDetectionAt = DateTime.UtcNow;
                var now = _lastDetectionAt;
                if (result == _lastResult && now - _lastScanAt < ScanCooldown)
                {
                    return;
                }

                _lastResult = result;
                _lastScanAt = now;
                ScheduleIdleReset();

                if (!OrderRegex.IsMatch(result))
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (!string.Equals(OrderEntry.Text, result, StringComparison.Ordinal))
                    {
                        OrderEntry.Text = result;
                    }
                    else
                    {
                        return;
                    }

                    StatusLabel.Text = $"Leído: {result}";
                    DetectionLabel.Text = "Orden detectada.";
                    await ShowDetectedAsync(result);
                });
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al procesar lectura: {ex.Message}";
            }
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

        private async void OnBackClicked(object? sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//Scanner");
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al volver: {ex.Message}";
            }
        }

        private void OnOrderChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            if (OrderRegex.IsMatch(e.NewTextValue))
            {
                StatusLabel.Text = "Orden lista para rework.";
            }
        }

        private async void OnRegisterClicked(object? sender, EventArgs e)
        {
            if (!_session.IsLoggedIn)
            {
                StatusLabel.Text = "Inicia sesión para registrar.";
                return;
            }

            var order = OrderEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(order))
            {
                StatusLabel.Text = "Captura la orden.";
                return;
            }

            if (!uint.TryParse(QuantityEntry.Text, out var quantity) || quantity == 0)
            {
                StatusLabel.Text = "Cantidad inválida para rework.";
                return;
            }

            if (!await _registerLock.WaitAsync(0))
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

                var reason = await DisplayPromptAsync("Rework", "Motivo de rework (opcional):");
                var release = await DisplayAlert("Rework", "¿Liberar la orden de rework?", "Sí", "No");

                var response = await _apiClient.ReworkAsync(new ReworkRequest
                {
                    WorkOrderNumber = order,
                    Quantity = quantity,
                    UserId = _session.UserId,
                    DeviceId = _session.DeviceId,
                    Reason = reason,
                    Completed = release
                }, CancellationToken.None);

                StatusLabel.Text = response.Message;
                DetectionLabel.Text = $"Estado WIP: {response.WipStatus}";
                QuantityEntry.Text = string.Empty;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"No se pudo registrar rework: {ex.Message}";
            }
            finally
            {
                _isProcessing = false;
                await SetLoadingStateAsync(false);
                _registerLock.Release();
            }
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
                    TryInverted = false,
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
            CancelIdleReset();
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
        }

        private void StopScanner()
        {
            DisposeScanner();
            CancelDetectedOverlay();
        }

        private void ScheduleIdleReset()
        {
            CancelIdleReset();
            _idleResetCts = new CancellationTokenSource();
            _ = ResetAfterIdleAsync(_idleResetCts.Token);
        }

        private void CancelIdleReset()
        {
            _idleResetCts?.Cancel();
            _idleResetCts = null;
        }

        private async Task ResetAfterIdleAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(IdleResetDelay, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_barcodeReader is not null)
                    {
                        _barcodeReader.IsDetecting = true;
                    }

                    _lastResult = null;
                    _lastScanAt = DateTime.MinValue;
                    _lastDetectionAt = DateTime.MinValue;
                    StatusLabel.Text = "Escaneo rework activo";
                    DetectionLabel.Text = "Listo para detectar órdenes.";
                });
            }
            catch (TaskCanceledException)
            {
                // Ignore cancelled reset
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

        private Task SetLoadingStateAsync(bool isLoading)
        {
            return MainThread.InvokeOnMainThreadAsync(() =>
            {
                LoadingOverlay.IsVisible = isLoading;
                LoadingIndicator.IsRunning = isLoading;
            });
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
    }
}
