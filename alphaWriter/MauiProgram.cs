using alphaWriter.Services;
using alphaWriter.Services.Nlp;
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

            // NLP Services
            builder.Services.AddSingleton<IStyleAnalyzer, StyleAnalyzer>();
            builder.Services.AddSingleton<IPacingAnalyzer, PacingAnalyzer>();
            builder.Services.AddSingleton<INlpModelManager, NlpModelManager>();
            builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
            builder.Services.AddSingleton<IEmotionService, EmotionService>();
            builder.Services.AddSingleton<ICharacterVoiceAnalyzer, CharacterVoiceAnalyzer>();
            builder.Services.AddSingleton<INlpCacheService, NlpCacheService>();
            builder.Services.AddSingleton<IPosTaggingService, PosTaggingService>();
            builder.Services.AddSingleton<INerService, NerService>();
            builder.Services.AddSingleton<ILocationHeuristicService, LocationHeuristicService>();
            builder.Services.AddSingleton<INlpAnalysisService>(sp =>
                new NlpAnalysisService(
                    sp.GetRequiredService<IStyleAnalyzer>(),
                    sp.GetRequiredService<IPacingAnalyzer>(),
                    sp.GetRequiredService<IEmbeddingService>(),
                    sp.GetRequiredService<INlpModelManager>(),
                    sp.GetRequiredService<IEmotionService>(),
                    sp.GetRequiredService<ICharacterVoiceAnalyzer>(),
                    sp.GetRequiredService<IPosTaggingService>()));

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
