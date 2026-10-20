using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace alphaWriter.Services.Nlp
{
    /// <summary>
    /// Wraps a POS-tagging pipeline (Catalyst) for per-token part-of-speech labels.
    /// Pos values follow Universal Dependencies: VERB, AUX, ADJ, ADV, NOUN, etc.
    /// </summary>
    public interface IPosTaggingService : IDisposable
    {
        bool IsLoaded { get; }

        /// <summary>Downloads (first run) and loads the English POS model.</summary>
        Task LoadAsync(CancellationToken ct = default);

        /// <summary>Releases the pipeline from memory; reloadable via LoadAsync.</summary>
        void UnloadModel();

        /// <summary>
        /// Tags each sentence and returns an array — one element per sentence —
        /// of (Value, Pos) token tuples.
        /// </summary>
        IReadOnlyList<(string Value, string Pos)>[] TagSentences(IReadOnlyList<string> sentences);

        /// <summary>
        /// Tags each sentence and returns, per sentence, (Start, Length, Pos) spans whose
        /// offsets are relative to that sentence's text. Used to align tags with
        /// SceneDocument tokens by character position.
        /// </summary>
        IReadOnlyList<(int Start, int Length, string Pos)>[] TagSpans(IReadOnlyList<string> sentences);
    }
}
