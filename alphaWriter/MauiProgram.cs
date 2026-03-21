using alphaWriter.Services;
using alphaWriter.ViewModels;
using alphaWriter.Views;
using Microsoft.Extensions.Logging;

namespace alphaWriter
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Services
            builder.Services.AddSingleton<IBookService, BookService>();
            builder.Services.AddSingleton<IImageService, ImageService>();

            // ViewModels
            builder.Services.AddSingleton<WriterViewModel>();

            // Pages & Shell
            builder.Services.AddSingleton<WriterPage>();
            builder.Services.AddSingleton<AppShell>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
