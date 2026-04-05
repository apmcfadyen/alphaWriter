using Catalyst;
using Mosaik.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    /// <summary>
    /// Wraps the Catalyst NLP pipeline for English POS tagging.
    /// </summary>
    public class PosTaggingService : IPosTaggingService
    {
        private Pipeline? _pipeline;

        public bool IsLoaded => _pipeline is not null;

        public Task LoadAsync(CancellationToken ct = default)
        {
            if (IsLoaded) return Task.CompletedTask;

            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await CatalystInitializer.EnsureInitializedAsync(ct);
                ct.ThrowIfCancellationRequested();
                _pipeline = await Pipeline.ForAsync(Language.English);
                if (_pipeline is null)
                    throw new InvalidOperationException(
                        "Catalyst pipeline returned null — models may not have been registered.");
            }, ct);
        }

        public void UnloadModel()
        {
            _pipeline = null;
        }

        public IReadOnlyList<(string Value, string Pos)>[] TagSentences(
            IReadOnlyList<string> sentences)
        {
            if (!IsLoaded)
                throw new InvalidOperationException(
                    "Model not loaded. Call LoadAsync first.");

            var results = new IReadOnlyList<(string Value, string Pos)>[sentences.Count];

            for (int i = 0; i < sentences.Count; i++)
            {
                var doc = new Document(sentences[i], Language.English);
                _pipeline!.ProcessSingle(doc);

                var tokens = new List<(string Value, string Pos)>();
                foreach (var span in doc)
                    foreach (var token in span)
                        tokens.Add((token.Value, token.POS.ToString()));

                results[i] = tokens;
            }

            return results;
        }

        public void Dispose()
        {
            _pipeline = null;
        }
    }
}
