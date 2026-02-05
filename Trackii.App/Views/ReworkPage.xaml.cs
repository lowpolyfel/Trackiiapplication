using System.Text.RegularExpressions;
using System.Threading;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;

namespace Trackii.App.Views
{
    [QueryProperty(nameof(InitialOrderNumber), "order")]
    public partial class ReworkPage : ContentPage
    {
        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(500);
        private object? _nativeCameraView;
        private EventInfo? _nativeDetectionEvent;
        private Delegate? _nativeDetectionHandler;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
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
            _isProcessing = false;
            StartScanner();
            StartScanAnimation();

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
            base.OnDisappearing();
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
                SetScannerIsDetecting(true);
                await SetLoadingStateAsync(false);
                _registerLock.Release();
            }
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
            SetProperty(cameraType, view, "CameraEnabled", true);
            ConfigureCode128Only(cameraType, view);
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

            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BarcodeScanning.Native.Maui");
            return assembly?.GetTypes().FirstOrDefault(t => t.Name == "CameraView" && typeof(View).IsAssignableFrom(t));
        }

        private static void ConfigureCode128Only(Type cameraType, object cameraInstance)
        {
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

            var method = typeof(ReworkPage).GetMethod(nameof(OnNativeDetectionEvent), BindingFlags.Instance | BindingFlags.NonPublic)!;
            var body = Expression.Call(Expression.Constant(this), method, Expression.Convert(argsParameter, typeof(object)));

            return Expression.Lambda(delegateType, body, senderParameter, argsParameter).Compile();
        }

        private void OnNativeDetectionEvent(object args)
        {
            var detected = ExtractDetectedBarcode(args);
            if (detected is null || string.IsNullOrWhiteSpace(detected.Value))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(detected.Format) && !string.Equals(detected.Format, "Code128", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(async () => await ProcessDetectedTextAsync(detected.Value.Trim()));
        }

        private async Task ProcessDetectedTextAsync(string result)
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

            _isProcessing = true;
            _lastResult = result;
            _lastScanAt = now;
            SetScannerIsDetecting(false);

            if (!OrderRegex.IsMatch(result))
            {
                return;
            }

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
            var value = prop?.GetValue(instance);
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
            if (_nativeCameraView is null)
            {
                return;
            }

            var type = _nativeCameraView.GetType();
            SetProperty(type, _nativeCameraView, "IsDetecting", value);
            SetProperty(type, _nativeCameraView, "IsScanning", value);
            SetProperty(type, _nativeCameraView, "IsEnabled", true);
            SetProperty(type, _nativeCameraView, "CameraEnabled", true);
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

        private void StartScanner()
        {
            if (!_hasPermission)
            {
                return;
            }

            if (ScannerHost.Content is null)
            {
                BuildScanner();
            }

            SetScannerIsDetecting(true);
        }

        private void StopScanner()
        {
            SetScannerIsDetecting(false);
            CancelDetectedOverlay();
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

        private sealed record DetectedBarcode(string Value, string? Format);
    }
}
