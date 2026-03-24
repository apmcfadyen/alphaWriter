using System;
using System.Threading;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public interface INlpModelManager
    {
        string ModelsDirectory { get; }
        bool AreModelsAvailable { get; }
        bool IsEmbeddingModelAvailable { get; }
        bool IsEmotionModelAvailable { get; }

        string GetModelPath(string modelName);
        string GetTokenizerPath(string modelName);
        string GetMergesPath(string modelName);

        Task DownloadModelsAsync(IProgress<(string model, double percent)>? progress = null,
            CancellationToken ct = default);
    }
}
