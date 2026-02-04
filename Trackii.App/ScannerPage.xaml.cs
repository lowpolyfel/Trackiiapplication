using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Trackii.App.Models;
using Trackii.App.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace Trackii.App
{
    public partial class ScannerPage : ContentPage
    {
        private enum ThemePreset
        {
            Clean,
            Glass,
            Soft,
            Contrast
        }

        private static readonly Regex OrderRegex = new("^\\d{7}$", RegexOptions.Compiled);
        private static readonly TimeSpan ScanCooldown = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan IdleResetDelay = TimeSpan.FromSeconds(3);
        private CameraBarcodeReaderView? _barcodeReader;
        private CancellationTokenSource? _animationCts;
        private CancellationTokenSource? _detectedCts;
        private CancellationTokenSource? _idleResetCts;
        private readonly AppSession _session;
        private readonly ApiClient _apiClient;
        private readonly SemaphoreSlim _scanLock = new(1, 1);
        private bool _isProcessing;
        private string? _lastResult;
        private DateTime _lastScanAt;
        private DateTime _lastDetectionAt;
        private bool _hasPermission;
        private uint? _maxQuantity;
        private PartLookupResponse? _partInfo;
        private WorkOrderContextResponse? _workOrderContext;

        public ScannerPage()
        {
            InitializeComponent();
            _session = App.Services.GetRequiredService<AppSession>();
            _apiClient = App.Services.GetRequiredService<ApiClient>();
            BuildScanner();
            ApplyTheme(ThemePreset.Clean);
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
            StatusLabel.Text = "Escaneo instantáneo activo";
            DetectionLabel.Text = "Listo para detectar códigos.";
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

        private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            var result = e.Results?.FirstOrDefault()?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                return;
            }

            try
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

                _lastResult = result;
                _lastScanAt = now;
                ScheduleIdleReset();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    StatusLabel.Text = $"Leído: {result}";
                    DetectionLabel.Text = "Detectado al instante.";
                    await ShowDetectedAsync(result);
                    if (OrderRegex.IsMatch(result))
                    {
                        if (!string.Equals(OrderEntry.Text, result, StringComparison.Ordinal))
                        {
                            OrderEntry.Text = result;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (!string.Equals(PartEntry.Text, result, StringComparison.Ordinal))
                        {
                            PartEntry.Text = result;
                        }
                        else
                        {
                            return;
                        }
                    }

                    await TryFinalizeScanAsync();
                });
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al procesar lectura: {ex.Message}";
            }
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
                    StatusLabel.Text = "Escaneo instantáneo activo";
                    DetectionLabel.Text = "Listo para detectar códigos.";
                });
            }
            catch (TaskCanceledException)
            {
                // Ignore cancelled reset
            }
        }

        private void UpdateHeader()
        {
            if (_session.IsLoggedIn)
            {
                AuthTitleLabel.Text = _session.DeviceName;
                AuthSubtitleLabel.Text = _session.LocationName;
                AuthCard.BackgroundColor = Color.FromArgb("#F1F5F9");
                LoginButton.IsVisible = false;
            }
            else
            {
                AuthTitleLabel.Text = "Sin asignar";
                AuthSubtitleLabel.Text = "Sin asignar";
                AuthCard.BackgroundColor = Color.FromArgb("#F1F5F9");
                LoginButton.IsVisible = true;
            }
        }

        private void ApplyTheme(ThemePreset preset)
        {
            switch (preset)
            {
                case ThemePreset.Clean:
                    SetThemeResources(
                        page: "#F8FAFC",
                        card: "#FFFFFF",
                        status: "#F1F5F9",
                        primary: "#0F172A",
                        secondary: "#475569",
                        muted: "#64748B",
                        accent: "#FF3B30",
                        scannerShade: "#0F172A",
                        register: "#22C55E",
                        scrap: "#F97316",
                        rework: "#3B82F6",
                        neutral: "#E2E8F0",
                        neutralText: "#0F172A");
                    HighlightThemeButton(ThemeOneButton);
                    break;
                case ThemePreset.Glass:
                    SetThemeResources(
                        page: "#F1F5F9",
                        card: "#F8FAFC",
                        status: "#E2E8F0",
                        primary: "#0B1220",
                        secondary: "#3E4C63",
                        muted: "#64748B",
                        accent: "#F43F5E",
                        scannerShade: "#0B1220",
                        register: "#10B981",
                        scrap: "#FB7185",
                        rework: "#6366F1",
                        neutral: "#CBD5F5",
                        neutralText: "#0B1220");
                    HighlightThemeButton(ThemeTwoButton);
                    break;
                case ThemePreset.Soft:
                    SetThemeResources(
                        page: "#FFF7ED",
                        card: "#FFFFFF",
                        status: "#FFE4C7",
                        primary: "#1F2937",
                        secondary: "#6B7280",
                        muted: "#9CA3AF",
                        accent: "#EA580C",
                        scannerShade: "#1F2937",
                        register: "#16A34A",
                        scrap: "#F59E0B",
                        rework: "#0EA5E9",
                        neutral: "#FEF3C7",
                        neutralText: "#1F2937");
                    HighlightThemeButton(ThemeThreeButton);
                    break;
                case ThemePreset.Contrast:
                    SetThemeResources(
                        page: "#EEF2FF",
                        card: "#FFFFFF",
                        status: "#E0E7FF",
                        primary: "#111827",
                        secondary: "#4B5563",
                        muted: "#6B7280",
                        accent: "#DC2626",
                        scannerShade: "#111827",
                        register: "#059669",
                        scrap: "#DB2777",
                        rework: "#2563EB",
                        neutral: "#E5E7EB",
                        neutralText: "#111827");
                    HighlightThemeButton(ThemeFourButton);
                    break;
            }
        }

        private void SetThemeResources(
            string page,
            string card,
            string status,
            string primary,
            string secondary,
            string muted,
            string accent,
            string scannerShade,
            string register,
            string scrap,
            string rework,
            string neutral,
            string neutralText)
        {
            Resources["PageBackground"] = Color.FromArgb(page);
            Resources["CardBackground"] = Color.FromArgb(card);
            Resources["StatusBackground"] = Color.FromArgb(status);
            Resources["PrimaryText"] = Color.FromArgb(primary);
            Resources["SecondaryText"] = Color.FromArgb(secondary);
            Resources["MutedText"] = Color.FromArgb(muted);
            Resources["AccentRed"] = Color.FromArgb(accent);
            Resources["ScannerShade"] = Color.FromArgb(scannerShade);
            Resources["RegisterButton"] = Color.FromArgb(register);
            Resources["ScrapButton"] = Color.FromArgb(scrap);
            Resources["ReworkButton"] = Color.FromArgb(rework);
            Resources["NeutralButton"] = Color.FromArgb(neutral);
            Resources["NeutralButtonText"] = Color.FromArgb(neutralText);
        }

        private void HighlightThemeButton(Button selectedButton)
        {
            var buttons = new[] { ThemeOneButton, ThemeTwoButton, ThemeThreeButton, ThemeFourButton };
            foreach (var button in buttons)
            {
                button.BackgroundColor = Resources.TryGetValue("NeutralButton", out var neutral)
                    ? (Color)neutral
                    : Color.FromArgb("#E2E8F0");
                button.TextColor = Resources.TryGetValue("NeutralButtonText", out var neutralText)
                    ? (Color)neutralText
                    : Color.FromArgb("#0F172A");
            }

            selectedButton.BackgroundColor = Resources.TryGetValue("AccentRed", out var accent)
                ? (Color)accent
                : Color.FromArgb("#FF3B30");
            selectedButton.TextColor = Colors.White;
        }

        private void OnThemeOneClicked(object? sender, EventArgs e) => ApplyTheme(ThemePreset.Clean);

        private void OnThemeTwoClicked(object? sender, EventArgs e) => ApplyTheme(ThemePreset.Glass);

        private void OnThemeThreeClicked(object? sender, EventArgs e) => ApplyTheme(ThemePreset.Soft);

        private void OnThemeFourClicked(object? sender, EventArgs e) => ApplyTheme(ThemePreset.Contrast);

        private async void OnOrderChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            if (OrderRegex.IsMatch(e.NewTextValue))
            {
                await TryFinalizeScanAsync();
            }
        }

        private async void OnPartChanged(object? sender, TextChangedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            await TryFinalizeScanAsync();
        }

        private async Task LoadPartInfoAsync(string partNumber)
        {
            try
            {
                var response = await _apiClient.GetPartInfoAsync(partNumber, CancellationToken.None);
                _partInfo = response;
                if (!response.Found)
                {
                    AreaEntry.Text = "-";
                    FamilyEntry.Text = "-";
                    SubfamilyEntry.Text = "-";
                    await DisplayAlert("Producto no encontrado", response.Message ?? "Producto no registrado.", "OK");
                    return;
                }

                AreaEntry.Text = response.AreaName ?? "-";
                FamilyEntry.Text = response.FamilyName ?? "-";
                SubfamilyEntry.Text = response.SubfamilyName ?? "-";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al consultar producto: {ex.Message}";
            }
        }

        private async Task LoadWorkOrderContextAsync(string workOrderNumber)
        {
            if (!_session.IsLoggedIn)
            {
                StatusLabel.Text = "Inicia sesión para continuar.";
                return;
            }

            try
            {
                var response = await _apiClient.GetWorkOrderContextAsync(workOrderNumber, _session.DeviceId, CancellationToken.None);
                _workOrderContext = response;
                if (!response.Found)
                {
                    _maxQuantity = null;
                    QuantityEntry.Text = string.Empty;
                    QuantityHintLabel.Text = _session.LocationId == 1
                        ? "Orden no encontrada. Alloy puede crearla al registrar."
                        : response.Message ?? "Orden no encontrada.";
                    return;
                }

                if (!response.CanProceed)
                {
                    _maxQuantity = null;
                    QuantityEntry.Text = string.Empty;
                    QuantityHintLabel.Text = response.Message ?? "No se puede avanzar.";
                    return;
                }

                _maxQuantity = response.MaxQty;
                if (response.IsFirstStep)
                {
                    QuantityEntry.Text = string.Empty;
                    QuantityHintLabel.Text = "Primera etapa: ingresa la cantidad.";
                }
                else
                {
                    QuantityEntry.Text = response.PreviousQty?.ToString() ?? string.Empty;
                    QuantityHintLabel.Text = response.MaxQty is null
                        ? "Cantidad previa no disponible."
                        : $"Cantidad máxima: {response.MaxQty}";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error al consultar orden: {ex.Message}";
            }
        }

        private async Task TryFinalizeScanAsync()
        {
            var order = OrderEntry.Text?.Trim();
            var part = PartEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(order) || string.IsNullOrWhiteSpace(part))
            {
                return;
            }

            if (!await _scanLock.WaitAsync(0))
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

                await LoadWorkOrderContextAsync(order);
                await LoadPartInfoAsync(part);
            }
            finally
            {
                _isProcessing = false;
                await SetLoadingStateAsync(false);
                _scanLock.Release();
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

        private void OnQuantityChanged(object? sender, TextChangedEventArgs e)
        {
            if (_maxQuantity is null || string.IsNullOrWhiteSpace(e.NewTextValue))
            {
                return;
            }

            if (uint.TryParse(e.NewTextValue, out var value) && value > _maxQuantity.Value)
            {
                QuantityEntry.Text = _maxQuantity.Value.ToString();
                QuantityHintLabel.Text = $"Cantidad máxima: {_maxQuantity}";
            }
        }

        private async void OnRegisterClicked(object? sender, EventArgs e)
        {
            if (!_session.IsLoggedIn)
            {
                StatusLabel.Text = "Inicia sesión para registrar.";
                return;
            }

            if (_partInfo is null || !_partInfo.Found)
            {
                StatusLabel.Text = "Producto no válido.";
                return;
            }

            if (_workOrderContext is null)
            {
                StatusLabel.Text = "Orden no válida.";
                return;
            }

            if (!_workOrderContext.Found && _session.LocationId != 1)
            {
                StatusLabel.Text = _workOrderContext.Message ?? "Orden no encontrada.";
                return;
            }

            if (_workOrderContext.Found && !_workOrderContext.CanProceed)
            {
                StatusLabel.Text = _workOrderContext.Message ?? "Orden no válida.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OrderEntry.Text) || string.IsNullOrWhiteSpace(PartEntry.Text))
            {
                StatusLabel.Text = "Captura orden y parte.";
                return;
            }

            if (!uint.TryParse(QuantityEntry.Text, out var quantity) || quantity == 0)
            {
                StatusLabel.Text = "Cantidad inválida.";
                return;
            }

            if (_maxQuantity is not null && quantity > _maxQuantity.Value)
            {
                StatusLabel.Text = "Cantidad mayor a la permitida.";
                return;
            }

            try
            {
                var response = await _apiClient.RegisterScanAsync(new RegisterScanRequest
                {
                    WorkOrderNumber = OrderEntry.Text.Trim(),
                    PartNumber = PartEntry.Text.Trim(),
                    Quantity = quantity,
                    UserId = _session.UserId,
                    DeviceId = _session.DeviceId
                }, CancellationToken.None);
                StatusLabel.Text = response.Message;
                DetectionLabel.Text = "Registro exitoso.";
                ResetForm();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"No se pudo registrar: {ex.Message}";
            }
        }

        private async void OnScrapClicked(object? sender, EventArgs e)
        {
            if (!_session.IsLoggedIn)
            {
                StatusLabel.Text = "Inicia sesión para cancelar.";
                return;
            }

            if (string.IsNullOrWhiteSpace(OrderEntry.Text))
            {
                StatusLabel.Text = "Captura la orden.";
                return;
            }

            try
            {
                var orderNumber = OrderEntry.Text.Trim();
                await LoadWorkOrderContextAsync(orderNumber);

                if (_workOrderContext is null || !_workOrderContext.Found)
                {
                    StatusLabel.Text = _workOrderContext?.Message ?? "Orden no encontrada.";
                    return;
                }

                var status = _workOrderContext.WorkOrderStatus?.Trim().ToUpperInvariant();
                if (status is "CANCELLED" or "FINISHED")
                {
                    StatusLabel.Text = "La orden no está activa.";
                    return;
                }

                if (!_workOrderContext.CanProceed)
                {
                    StatusLabel.Text = _workOrderContext.Message ?? "La orden no está activa.";
                    return;
                }

                var response = await _apiClient.ScrapAsync(new ScrapRequest
                {
                    WorkOrderNumber = orderNumber,
                    UserId = _session.UserId,
                    DeviceId = _session.DeviceId,
                    Reason = "Scrap desde scanner"
                }, CancellationToken.None);
                StatusLabel.Text = response.Message;
                DetectionLabel.Text = "Orden cancelada.";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"No se pudo cancelar: {ex.Message}";
            }
        }

        private async void OnReworkClicked(object? sender, EventArgs e)
        {
            try
            {
                var orderNumber = OrderEntry.Text?.Trim();
                var route = string.IsNullOrWhiteSpace(orderNumber)
                    ? nameof(ReworkPage)
                    : $"{nameof(ReworkPage)}?order={Uri.EscapeDataString(orderNumber)}";
                await Shell.Current.GoToAsync(route);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"No se pudo abrir rework: {ex.Message}";
            }
        }

        private void OnResetClicked(object? sender, EventArgs e)
        {
            ResetForm();
            StatusLabel.Text = "Formulario limpiado.";
            DetectionLabel.Text = "Listo para detectar códigos.";
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

                    await Task.WhenAll(
                        ScanLine.TranslateTo(0, travel, 1200, Easing.CubicInOut),
                        ScanGlow.TranslateTo(0, travel, 1200, Easing.CubicInOut));
                    await Task.WhenAll(
                        ScanLine.TranslateTo(0, 0, 1200, Easing.CubicInOut),
                        ScanGlow.TranslateTo(0, 0, 1200, Easing.CubicInOut));
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
                DetectedPopup.IsVisible = true;
                DetectedPopup.Opacity = 0;
                DetectedCard.Opacity = 0;
                DetectedCard.Scale = 0.92;
                await Task.WhenAll(
                    DetectedPopup.FadeTo(1, 120, Easing.CubicOut),
                    DetectedCard.FadeTo(1, 120, Easing.CubicOut),
                    DetectedCard.ScaleTo(1, 120, Easing.CubicOut));
                await Task.Delay(500, token);
                await DetectedPopup.FadeTo(0, 300, Easing.CubicIn);
                DetectedPopup.IsVisible = false;
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
            if (DetectedPopup is not null)
            {
                DetectedPopup.IsVisible = false;
                DetectedPopup.Opacity = 0;
            }
        }

        private void OnClearClicked(object? sender, EventArgs e)
        {
            ResetForm();
            StatusLabel.Text = "Formulario limpio.";
            DetectionLabel.Text = "Listo para detectar códigos.";
        }

        private void ResetForm()
        {
            OrderEntry.Text = string.Empty;
            PartEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            QuantityHintLabel.Text = string.Empty;
            AreaEntry.Text = "-";
            FamilyEntry.Text = "-";
            SubfamilyEntry.Text = "-";
            _partInfo = null;
            _workOrderContext = null;
            _maxQuantity = null;
        }

        private void OnClearClicked(object? sender, EventArgs e)
        {
            ResetForm();
            StatusLabel.Text = "Formulario limpio.";
            DetectionLabel.Text = "Listo para detectar códigos.";
        }
    }
}
