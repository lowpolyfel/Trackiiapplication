using Microsoft.Extensions.DependencyInjection;

namespace Trackii.App
{
    public partial class App : Application
    {
        public static IServiceProvider Services =>
            Current?.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("Servicios MAUI no disponibles.");

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
