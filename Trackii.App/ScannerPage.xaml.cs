using System.Text.RegularExpressions;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Trackii.App
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MonitorInterval = TimeSpan.FromMilliseconds(700);

        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _scannerCts;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private readonly AppSession _session;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;

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

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(result))
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

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                StatusLabel.Text = $"Leído: {result}";
                DetectionLabel.Text = "Detectado al instante.";
                _ = ShowDetectedAsync(result);
                if (OrderRegex.IsMatch(result))
                {
                    OrderEntry.Text = result;
                }
                else
                {
                    PartEntry.Text = result;
                }
            });
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

            RestartScanner("Reenfocando...");
        }

        private async void OnLoginClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("Login");
        }

        private void BuildScanner()
        {
            var reader = new CameraBarcodeReaderView
            {
                CameraLocation = CameraLocation.Rear,
                IsDetecting = false,
                Options = new BarcodeReaderOptions
                {
                    AutoRotate = false,
                    TryHarder = false,
                    TryInverted = false,
                    Multiple = false,
                    Formats = BarcodeFormats.All
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
            _lastDetectionAt = DateTime.UtcNow;

            if (_barcodeReader is not null)
            {
                _barcodeReader.IsDetecting = true;
            }

            _scannerCts = new CancellationTokenSource();
            _ = MonitorScannerAsync(_scannerCts.Token);
        }

        private void StopScanner()
        {
            _scannerCts?.Cancel();
            _scannerCts = null;

            DisposeScanner();
            CancelDetectedOverlay();
        }

        private void RestartScanner(string status)
        {
            if (!_hasPermission)
            {
                return;
            }

            DisposeScanner();
            BuildScanner();
            _lastDetectionAt = DateTime.UtcNow;

            if (_barcodeReader is not null)
            {
                _barcodeReader.IsDetecting = true;
            }

            StatusLabel.Text = status;
        }

        private async Task MonitorScannerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(MonitorInterval, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (DateTime.UtcNow - _lastDetectionAt < RestartThreshold)
                {
                    continue;
                }

                MainThread.BeginInvokeOnMainThread(() => RestartScanner("Reiniciando cámara..."));
            }
        }

        private void UpdateHeader()
        {
            if (_session.IsLoggedIn)
            {
                AuthTitleLabel.Text = _session.DeviceName;
                AuthSubtitleLabel.Text = $"{_session.LocationName} • {_session.Username}";
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
