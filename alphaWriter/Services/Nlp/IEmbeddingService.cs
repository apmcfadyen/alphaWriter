using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    public interface IEmbeddingService : IDisposable
    {
        bool IsLoaded { get; }
        Task LoadModelAsync(CancellationToken ct = default);
        float[] ComputeEmbedding(string text);
        float[][] ComputeEmbeddings(IReadOnlyList<string> texts);
        static float CosineSimilarity(float[] a, float[] b) => EmbeddingService.CosineSimilarityStatic(a, b);

        /// <summary>
        /// Releases the ONNX session and tokenizer to free memory.
        /// The model can be reloaded with LoadModelAsync.
        /// </summary>
        void UnloadModel();
    }
}
