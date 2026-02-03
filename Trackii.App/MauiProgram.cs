using Microsoft.Extensions.Logging;
using Trackii.App.Configuration;
using Trackii.App.Services;
using ZXing.Net.Maui;

namespace Trackii.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton(new HttpClient
            {
                BaseAddress = new Uri(AppConfig.ApiBaseUrl)
            });
            builder.Services.AddSingleton<ApiClient>();
#if ANDROID
            builder.Services.AddSingleton<IDeviceIdService, Platforms.Android.DeviceIdService>();
#else
            builder.Services.AddSingleton<IDeviceIdService, DefaultDeviceIdService>();
#endif

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
