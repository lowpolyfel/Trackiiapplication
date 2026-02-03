using System.Text.RegularExpressions;
using Microsoft.Maui.ApplicationModel;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Trackii.App
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(1);

        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _scannerCts;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;

        public ScannerPage()
        {
            InitializeComponent();
            BuildScanner();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var status = await Permissions.RequestAsync<Permissions.Camera>();
            _hasPermission = status == PermissionStatus.Granted;
            if (!_hasPermission)
            {
                StatusLabel.Text = "Permiso de cámara requerido.";
                StopScanner();
                return;
            }

            StatusLabel.Text = "Escaneando automáticamente...";
            StartScanner();
        }

        protected override void OnDisappearing()
        {
            StopScanner();
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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusLabel.Text = $"Leído: {result}";
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

        private void BuildScanner()
        {
            var reader = new CameraBarcodeReaderView
            {
                CameraLocation = CameraLocation.Rear,
                IsDetecting = false,
                Options = new BarcodeReaderOptions
                {
                    AutoRotate = true,
                    TryHarder = true,
                    TryInverted = true,
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
    }
}
