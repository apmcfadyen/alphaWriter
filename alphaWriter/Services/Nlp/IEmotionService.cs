using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp
{
    public interface IEmotionService : IDisposable
    {
        bool IsLoaded { get; }
        Task LoadModelAsync(CancellationToken ct = default);
        List<(EmotionLabel Label, float Confidence)> ClassifySentence(string text);

        /// <summary>
        /// Classifies multiple sentences in a single batched ONNX forward pass.
        /// Returns one result list per input text. Much faster than calling
        /// ClassifySentence in a loop for large scene analysis.
        /// </summary>
        List<List<(EmotionLabel Label, float Confidence)>> ClassifyBatch(IReadOnlyList<string> texts);

        /// <summary>
        /// Releases the ONNX session and tokenizer to free memory.
        /// The model can be reloaded with LoadModelAsync.
        /// </summary>
        void UnloadModel();
    }
}
