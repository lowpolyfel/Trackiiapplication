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
        private CameraBarcodeReaderView? _barcodeReader;
        private string? _lastResult;
        private DateTime _lastScanAt;

        public ScannerPage()
        {
            InitializeComponent();
            BuildScanner();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Permiso de cámara requerido.";
                if (_barcodeReader is not null)
                {
                    _barcodeReader.IsDetecting = false;
                }
                return;
            }

            StatusLabel.Text = "Listo para escanear";
            ResetScanner();
            await Task.Delay(250);
            if (_barcodeReader is not null)
            {
                _barcodeReader.IsDetecting = true;
            }
        }

        protected override void OnDisappearing()
        {
            if (_barcodeReader is not null)
            {
                _barcodeReader.IsDetecting = false;
            }

            base.OnDisappearing();
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            var now = DateTime.UtcNow;
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

            _barcodeReader.IsDetecting = false;
            _barcodeReader.IsDetecting = true;
            StatusLabel.Text = "Reiniciando escaneo...";
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

        private void ResetScanner()
        {
            if (_barcodeReader is not null)
            {
                _barcodeReader.BarcodesDetected -= OnBarcodesDetected;
            }

            ScannerHost.Content = null;
            _barcodeReader = null;
            BuildScanner();
        }
    }
}
