using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Trackii.App.Services;

namespace Trackii.App.Views
{
    public partial class ScannerPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(300);

        private readonly AppSession _session;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private EventInfo? _nativeDetectionEvent;
        private Delegate? _nativeDetectionHandler;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private bool _isNavigating;
        private bool _isProcessingDetection;
        private int _logoTapCount;

        public ScannerPage()
        {
            InitializeComponent();
            _session = App.Services.GetRequiredService<AppSession>();
            ConfigureScanner();
            AttachNativeDetectedHandler();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            UpdateHeader();

            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert("Permiso requerido", "Se requiere permiso de cámara para escanear.", "OK");
                StopScanner();
                return;
            }

            _isProcessingDetection = false;
            _lastResult = null;
            _lastScanAt = DateTime.MinValue;

            SetScannerIsDetecting(true);
            StartScanAnimation();
        }

        protected override void OnDisappearing()
        {
            StopScanner();
            StopScanAnimation();
            CancelDetectedOverlay();
            base.OnDisappearing();
        }

        private void UpdateHeader()
        {
            HeaderLocationLabel.Text = _session.IsLoggedIn ? $"Localidad: {_session.LocationName}" : "Localidad: sin sesión";
            HeaderDeviceLabel.Text = _session.IsLoggedIn ? $"Tableta: {_session.DeviceName}" : "Tableta: sin sesión";
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

        private void ConfigureScanner()
        {
            var cameraType = NativeCameraView.GetType();

            SetProperty(cameraType, NativeCameraView, "CaptureQuality", "Medium");
            SetProperty(cameraType, NativeCameraView, "ScanInterval", 50);
            SetProperty(cameraType, NativeCameraView, "IsDetecting", true);
            SetProperty(cameraType, NativeCameraView, "IsScanning", true);
            SetProperty(cameraType, NativeCameraView, "CameraEnabled", true);

            ConfigureCode128Only(cameraType, NativeCameraView);
        }

        private static void ConfigureCode128Only(Type cameraType, object cameraInstance)
        {
            foreach (var propertyName in new[] { "Formats", "BarcodeFormats", "AcceptedBarcodeFormats", "Symbologies" })
            {
                var property = cameraType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property is null || !property.CanWrite)
                {
                    continue;
                }

                try
                {
                    if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(cameraInstance, "Code128");
                        return;
                    }

                    if (property.PropertyType.IsEnum)
                    {
                        var enumValue = Enum.Parse(property.PropertyType, "Code128", true);
                        property.SetValue(cameraInstance, enumValue);
                        return;
                    }
                }
                catch
                {
                    // continue with next candidate property
                }
            }

            foreach (var optionsPropertyName in new[] { "Options", "ReaderOptions", "ScannerOptions" })
            {
                var optionsProp = cameraType.GetProperty(optionsPropertyName, BindingFlags.Public | BindingFlags.Instance);
                var options = optionsProp?.GetValue(cameraInstance);
                if (options is null)
                {
                    continue;
                }

                var optionsType = options.GetType();
                SetProperty(optionsType, options, "Multiple", false);
                SetProperty(optionsType, options, "AutoRotate", true);

                foreach (var formatPropertyName in new[] { "Formats", "BarcodeFormats", "AcceptedBarcodeFormats", "Symbologies" })
                {
                    var formatProp = optionsType.GetProperty(formatPropertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (formatProp is null || !formatProp.CanWrite)
                    {
                        continue;
                    }

                    try
                    {
                        if (formatProp.PropertyType == typeof(string))
                        {
                            formatProp.SetValue(options, "Code128");
                            return;
                        }

                        if (formatProp.PropertyType.IsEnum)
                        {
                            var enumValue = Enum.Parse(formatProp.PropertyType, "Code128", true);
                            formatProp.SetValue(options, enumValue);
                            return;
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }
                }
            }
        }

        private void AttachNativeDetectedHandler()
        {
            var cameraType = NativeCameraView.GetType();
            _nativeDetectionEvent = cameraType.GetEvent("BarcodesDetected")
                ?? cameraType.GetEvent("BarcodeDetected")
                ?? cameraType.GetEvent("DetectionFinished");

            if (_nativeDetectionEvent is null)
            {
                return;
            }

            var delegateType = _nativeDetectionEvent.EventHandlerType;
            if (delegateType is null)
            {
                return;
            }

            try
            {
                _nativeDetectionHandler = CreateDetectionDelegate(delegateType);
                _nativeDetectionEvent.AddEventHandler(NativeCameraView, _nativeDetectionHandler);
            }
            catch
            {
                _nativeDetectionEvent = null;
                _nativeDetectionHandler = null;
            }
        }

        private Delegate CreateDetectionDelegate(Type delegateType)
        {
            var invoke = delegateType.GetMethod("Invoke")!;
            var parameters = invoke.GetParameters();
            if (parameters.Length != 2)
            {
                throw new NotSupportedException("Unexpected native detection event signature.");
            }

            var senderParameter = Expression.Parameter(parameters[0].ParameterType, "sender");
            var argsParameter = Expression.Parameter(parameters[1].ParameterType, "args");

            var method = typeof(ScannerPage).GetMethod(nameof(OnNativeDetectionEvent), BindingFlags.Instance | BindingFlags.NonPublic)!;
            var body = Expression.Call(Expression.Constant(this), method, Expression.Convert(argsParameter, typeof(object)));

            return Expression.Lambda(delegateType, body, senderParameter, argsParameter).Compile();
        }

        private void OnNativeDetectionEvent(object args)
        {
            var detected = ExtractDetectedBarcode(args);
            if (detected is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(detected.Format) && !string.Equals(detected.Format, "Code128", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(detected.Value))
            {
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(async () => await ProcessDetectedTextAsync(detected.Value.Trim()));
        }

        private async Task ProcessDetectedTextAsync(string result)
        {
            if (_isNavigating || _isProcessingDetection)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (result == _lastResult && now - _lastScanAt < ScanCooldown)
            {
                return;
            }

            _isProcessingDetection = true;
            _lastResult = result;
            _lastScanAt = now;

            SetScannerIsDetecting(false);

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

        private static DetectedBarcode? ExtractDetectedBarcode(object args)
        {
            var queue = new Queue<object?>();
            queue.Enqueue(args);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current is null)
                {
                    continue;
                }

                if (current is string text && !string.IsNullOrWhiteSpace(text))
                {
                    return new DetectedBarcode(text.Trim(), null);
                }

                var type = current.GetType();

                var value = ReadStringProperty(type, current, "Value")
                    ?? ReadStringProperty(type, current, "RawValue")
                    ?? ReadStringProperty(type, current, "DisplayValue")
                    ?? ReadStringProperty(type, current, "Text");

                var format = ReadStringProperty(type, current, "Format")
                    ?? ReadStringProperty(type, current, "BarcodeType")
                    ?? ReadStringProperty(type, current, "Symbology");

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return new DetectedBarcode(value.Trim(), format);
                }

                foreach (var property in new[] { "Results", "Barcodes", "DetectedBarcodes", "Items" })
                {
                    var prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.GetValue(current) is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            queue.Enqueue(item);
                        }
                    }
                }
            }

            return null;
        }

        private static string? ReadStringProperty(Type type, object instance, string propertyName)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(instance) is null)
            {
                return null;
            }

            var value = prop.GetValue(instance);
            return value?.ToString();
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
                if (prop.PropertyType.IsEnum && value is string enumName)
                {
                    prop.SetValue(instance, Enum.Parse(prop.PropertyType, enumName, true));
                    return;
                }

                var converted = Convert.ChangeType(value, prop.PropertyType);
                prop.SetValue(instance, converted);
            }
            catch
            {
                // no-op
            }
        }

        private void SetScannerIsDetecting(bool value)
        {
            var type = NativeCameraView.GetType();
            SetProperty(type, NativeCameraView, "IsDetecting", value);
            SetProperty(type, NativeCameraView, "IsScanning", value);
            SetProperty(type, NativeCameraView, "IsEnabled", true);
            SetProperty(type, NativeCameraView, "CameraEnabled", value);
        }

        private void StopScanner()
        {
            SetScannerIsDetecting(false);
            CancelDetectedOverlay();
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
                    var travel = NativeCameraView.Height;
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

        private sealed record DetectedBarcode(string Value, string? Format);
    }
}
