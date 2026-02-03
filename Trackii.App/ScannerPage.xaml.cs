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
        private string? _lastResult;
        private DateTime _lastScanAt;

        public ScannerPage()
        {
            InitializeComponent();
            BarcodeReader.Options = new BarcodeReaderOptions
            {
                AutoRotate = true,
                TryHarder = true,
                TryInverted = true,
                Multiple = false,
                Formats = BarcodeFormats.All
            };
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Permiso de cámara requerido.";
                BarcodeReader.IsDetecting = false;
                return;
            }

            StatusLabel.Text = "Listo para escanear";
            BarcodeReader.IsDetecting = false;
            await Task.Delay(200);
            BarcodeReader.IsDetecting = true;
        }

        protected override void OnDisappearing()
        {
            BarcodeReader.IsDetecting = false;
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
            BarcodeReader.IsTorchOn = !BarcodeReader.IsTorchOn;
            StatusLabel.Text = BarcodeReader.IsTorchOn ? "Linterna encendida" : "Linterna apagada";
        }

        private void OnRefocusClicked(object? sender, EventArgs e)
        {
            BarcodeReader.IsDetecting = false;
            BarcodeReader.IsDetecting = true;
            StatusLabel.Text = "Reenfocando...";
        }
    }
}
