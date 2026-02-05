using Microsoft.Extensions.Logging;
using Trackii.App.Configuration;
using System.Reflection;
using Trackii.App.Services;

namespace Trackii.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseNativeBarcodeScanningPlugin()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton(new HttpClient
            {
                BaseAddress = new Uri(AppConfig.ApiBaseUrl)
            });
            builder.Services.AddSingleton<AppSession>();
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

        private static MauiAppBuilder UseNativeBarcodeScanningPlugin(this MauiAppBuilder builder)
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "BarcodeScanning.Native.Maui", StringComparison.Ordinal))
                ?? TryLoad("BarcodeScanning.Native.Maui");

            if (assembly is null)
            {
                return builder;
            }

            var extensionType = assembly
                .GetTypes()
                .FirstOrDefault(t => t.IsSealed && t.IsAbstract && t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(m => m.ReturnType == typeof(MauiAppBuilder)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(MauiAppBuilder)
                        && m.Name.StartsWith("Use", StringComparison.OrdinalIgnoreCase)));

            var method = extensionType?
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.ReturnType == typeof(MauiAppBuilder)
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(MauiAppBuilder)
                    && m.Name.StartsWith("Use", StringComparison.OrdinalIgnoreCase));

            if (method is not null)
            {
                _ = method.Invoke(null, new object[] { builder });
            }

            return builder;
        }

        private static System.Reflection.Assembly? TryLoad(string assemblyName)
        {
            try
            {
                return System.Reflection.Assembly.Load(assemblyName);
            }
            catch
            {
                return null;
            }
        }
    }
}
