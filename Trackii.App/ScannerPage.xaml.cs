using System.Text.RegularExpressions;
using ZXing.Net.Maui;

namespace Trackii.App
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);

        public ScannerPage()
        {
            InitializeComponent();
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
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
    }
}
