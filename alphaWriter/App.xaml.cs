using Microsoft.Extensions.DependencyInjection;

namespace alphaWriter
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            _services = services;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(_services.GetRequiredService<AppShell>());
            // Sync when the app comes back to the front and when it goes to the background
            var vm = _services.GetRequiredService<ViewModels.WriterViewModel>();
            window.Resumed += (_, _) => vm.OnAppResumed();
            window.Stopped += (_, _) => vm.OnAppStopped();
            return window;
        }
    }
}
