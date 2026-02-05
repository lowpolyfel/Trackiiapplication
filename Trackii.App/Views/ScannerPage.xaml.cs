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
        private static readonly TimeSpan IdleResetDelay = TimeSpan.FromSeconds(2);

        private readonly AppSession _session;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private CancellationTokenSource? _idleResetCts;
        private object? _nativeCameraView;
        private EventInfo? _nativeDetectionEvent;
        private Delegate? _nativeDetectionHandler;
        private string? _lastResult;
        private DateTime _lastScanAt;
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

        private void BuildScanner()
        {
            DisposeScanner();
            _nativeCameraView = CreateNativeCameraView();
            ScannerHost.Content = _nativeCameraView as View;
        }

        private View? CreateNativeCameraView()
        {
            var cameraType = ResolveCameraViewType();
            if (cameraType is null)
            {
                return null;
            }

            if (Activator.CreateInstance(cameraType) is not View view)
            {
                return null;
            }

            SetProperty(cameraType, view, "CaptureQuality", "Medium");
            SetProperty(cameraType, view, "ScanInterval", 50);
            SetProperty(cameraType, view, "IsDetecting", true);
            SetProperty(cameraType, view, "IsScanning", true);

            AttachNativeDetectedHandler(cameraType, view);
            return view;
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
                .FirstOrDefault(a => a.GetName().Name == "BarcodeScanning.Native.Maui");
            return assembly?.GetTypes().FirstOrDefault(t => t.Name == "CameraView" && typeof(View).IsAssignableFrom(t));
        }

        private void AttachNativeDetectedHandler(Type cameraType, object cameraInstance)
        {
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
                _nativeDetectionEvent.AddEventHandler(cameraInstance, _nativeDetectionHandler);
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
            var result = ExtractBarcodeValue(args);
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
                var now = DateTime.UtcNow;
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
                    return text.Trim();
                }

                var type = current.GetType();
                foreach (var property in new[] { "Value", "RawValue", "DisplayValue", "Text" })
                {
                    var prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.GetValue(current) is string value && !string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
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
            if (_nativeCameraView is null)
            {
                return;
            }

            var type = _nativeCameraView.GetType();
            SetProperty(type, _nativeCameraView, "IsDetecting", value);
            SetProperty(type, _nativeCameraView, "IsScanning", value);
            SetProperty(type, _nativeCameraView, "IsEnabled", value);
        }

        private void DisposeScanner()
        {
            if (_nativeCameraView is not null && _nativeDetectionEvent is not null && _nativeDetectionHandler is not null)
            {
                _nativeDetectionEvent.RemoveEventHandler(_nativeCameraView, _nativeDetectionHandler);
            }

            _nativeDetectionEvent = null;
            _nativeDetectionHandler = null;
            _nativeCameraView = null;
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
