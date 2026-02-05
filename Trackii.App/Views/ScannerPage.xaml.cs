using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Trackii.App.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Trackii.App.Views
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan IdleResetDelay = TimeSpan.FromSeconds(3);
        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private CancellationTokenSource? _idleResetCts;
        private readonly AppSession _session;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;
        private bool _isNavigating;
        private int _logoTapCount;

        public ScannerPage()
        {
            InitializeComponent();
            _session = App.Services.GetRequiredService<AppSession>();
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
                await DisplayAlert("Permiso requerido", "Se requiere permiso de cámara para escanear.", "OK");
                StopScanner();
                return;
            }

            StartScanner();
            StartScanAnimation();
            ScheduleIdleReset();
        }

        protected override void OnDisappearing()
        {
            StopScanner();
            StopScanAnimation();
            CancelDetectedOverlay();
            CancelIdleReset();
            base.OnDisappearing();
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
            try
            {
                await Shell.Current.GoToAsync("//Login");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Navegación", $"No se pudo abrir login: {ex.Message}", "OK");
            }
        }

        private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(result) || _isNavigating)
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
                ScheduleIdleReset();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ShowDetectedAsync(result);
                    if (OrderRegex.IsMatch(result))
                    {
                        OrderEntry.Text = result;
                    }
                    else
                    {
                        PartEntry.Text = result;
                    }

                    await TryGoToDetailsAsync();
                });
            }
            catch
            {
                // no-op, preserve scanning
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
                });
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }

        private async void OnOrderChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            await TryGoToDetailsAsync();
        }

        private async void OnPartChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            await TryGoToDetailsAsync();
        }

        private async Task TryGoToDetailsAsync()
        {
            var order = OrderEntry.Text?.Trim();
            var part = PartEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(order) || string.IsNullOrWhiteSpace(part) || _isNavigating)
            {
                return;
            }

            _isNavigating = true;
            try
            {
                StopScanner();
                StopScanAnimation();
                var route = $"{nameof(ScanDetailsPage)}?order={Uri.EscapeDataString(order)}&part={Uri.EscapeDataString(part)}";
                await Shell.Current.GoToAsync(route);
            }
            finally
            {
                _isNavigating = false;
            }
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
                await Task.Delay(300, token);
                await DetectedOverlay.FadeTo(0, 300, Easing.CubicIn);
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
            catch (ObjectDisposedException)
            {
                // ignore
            }
        }

        private void CancelDetectedOverlay()
        {
            _detectedCts?.Cancel();
            _detectedCts = null;
        }
    }
}
