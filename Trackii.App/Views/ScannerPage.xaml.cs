using System.Reflection;
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
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan IdleResetDelay = TimeSpan.FromSeconds(2);
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private CancellationTokenSource? _idleResetCts;
        private readonly AppSession _session;
        private object? _cameraView;
        private CameraBarcodeReaderView? _zxingReader;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;
        private bool _isNavigating;
        private bool _isProcessingDetection;
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

            if (ScannerHost.Content is null)
            {
                BuildScanner();
            }

            _isProcessingDetection = false;
            SetScannerIsDetecting(true);
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

        private void BuildScanner()
        {
            DisposeScanner();
            _cameraView = CreateNativeCameraView();
            if (_cameraView is View nativeView)
            {
                ScannerHost.Content = nativeView;
                return;
            }

            BuildZxingFallback();
        }

        private void BuildZxingFallback()
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
                    Multiple = false
                }
            };

            reader.BarcodesDetected += OnZxingBarcodesDetected;
            _zxingReader = reader;
            ScannerHost.Content = reader;
        }

        private View? CreateNativeCameraView()
        {
            try
            {
                var type = ResolveCameraViewType();
                if (type is null)
                {
                    return null;
                }

                if (Activator.CreateInstance(type) is not View view)
                {
                    return null;
                }

                SetProperty(type, view, "CaptureQuality", "Medium");
                SetProperty(type, view, "ScanInterval", 50);
                SetProperty(type, view, "IsDetecting", true);
                SetProperty(type, view, "IsScanning", true);
                SetProperty(type, view, "IsEnabled", true);
                TryAttachDetectedHandler(type, view);
                return view;
            }
            catch
            {
                return null;
            }
        }

        private static Type? ResolveCameraViewType()
        {
            var names = new[]
            {
                "BarcodeScanning.CameraView, BarcodeScanning.Native.Maui",
                "BarcodeScanning.Native.Maui.CameraView, BarcodeScanning.Native.Maui",
                "BarcodeScanning.Maui.CameraView, BarcodeScanning.Native.Maui"
            };

            foreach (var name in names)
            {
                var type = Type.GetType(name, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }

            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "BarcodeScanning.Native.Maui", StringComparison.Ordinal));
            return assembly?.GetTypes().FirstOrDefault(t => t.Name == "CameraView" && typeof(View).IsAssignableFrom(t));
        }

        private void TryAttachDetectedHandler(Type type, object instance)
        {
            var eventInfo = type.GetEvent("BarcodesDetected")
                ?? type.GetEvent("BarcodeDetected")
                ?? type.GetEvent("DetectionFinished");
            if (eventInfo is null)
            {
                return;
            }

            var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType!, this, nameof(OnNativeBarcodesDetected));
            eventInfo.AddEventHandler(instance, handler);
        }

        private void OnNativeBarcodesDetected(object? sender, object args)
        {
            var result = ExtractBarcodeValue(args);
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(async () => await ProcessDetectedTextAsync(result));
        }

        private void OnZxingBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(async () => await ProcessDetectedTextAsync(result));
        }

        private async Task ProcessDetectedTextAsync(string result)
        {
            if (_isNavigating || _isProcessingDetection)
            {
                return;
            }

            _isProcessingDetection = true;
            SetScannerIsDetecting(false);

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
            }
            finally
            {
                if (!_isNavigating)
                {
                    SetScannerIsDetecting(true);
                }

                _isProcessingDetection = false;
            }
        }

        private static string? ExtractBarcodeValue(object args)
        {
            if (args is null)
            {
                return null;
            }

            var candidates = new Queue<object?>();
            candidates.Enqueue(args);

            while (candidates.Count > 0)
            {
                var current = candidates.Dequeue();
                if (current is null)
                {
                    continue;
                }

                if (current is string raw && !string.IsNullOrWhiteSpace(raw))
                {
                    return raw.Trim();
                }

                var type = current.GetType();
                foreach (var propName in new[] { "Value", "RawValue", "DisplayValue", "Text" })
                {
                    var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.GetValue(current) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                foreach (var propName in new[] { "Results", "Barcodes", "DetectedBarcodes", "Items" })
                {
                    var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.GetValue(current) is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            candidates.Enqueue(item);
                        }
                    }
                }
            }

            return null;
        }

        private static void SetProperty(Type type, object instance, string propertyName, object value)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null || !prop.CanWrite)
            {
                return;
            }

            try
            {
                if (prop.PropertyType.IsEnum && value is string enumText)
                {
                    var enumValue = Enum.Parse(prop.PropertyType, enumText, true);
                    prop.SetValue(instance, enumValue);
                    return;
                }

                var converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(instance, converted);
            }
            catch
            {
                // best-effort compatibility
            }
        }

        private void SetScannerIsDetecting(bool value)
        {
            if (_zxingReader is not null)
            {
                _zxingReader.IsDetecting = value;
            }

            if (_cameraView is null)
            {
                return;
            }

            var type = _cameraView.GetType();
            SetProperty(type, _cameraView, "IsDetecting", value);
            SetProperty(type, _cameraView, "IsScanning", value);
            SetProperty(type, _cameraView, "IsEnabled", value);
        }

        private void DisposeScanner()
        {
            if (_zxingReader is not null)
            {
                _zxingReader.BarcodesDetected -= OnZxingBarcodesDetected;
                _zxingReader.IsDetecting = false;
                _zxingReader = null;
            }

            _cameraView = null;
            ScannerHost.Content = null;
        }

        private void StopScanner()
        {
            SetScannerIsDetecting(false);
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
                    SetScannerIsDetecting(true);
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
