using System.Collections.Generic;
using System.Linq;

namespace alphaWriter.Services.Nlp.Parsing
{
    public enum ParagraphKind { Narration, Dialogue, Mixed }

    /// <summary>
    /// One scene, analyzed once: normalized plain text broken into paragraphs, sentences
    /// and tokens, each carrying character offsets into <see cref="Text"/>. Every rule reads
    /// this instead of re-splitting or re-tagging the scene.
    /// </summary>
    /// <remarks>
    /// <see cref="Text"/> is the scene's plain text with comments stripped, whitespace inside
    /// each paragraph collapsed to single spaces, and paragraphs joined by '\n'.
    /// </remarks>
    public sealed class SceneDocument
    {
        public string Text { get; init; } = string.Empty;
        public List<ParagraphInfo> Paragraphs { get; init; } = [];
        public List<SentenceInfo> Sentences { get; init; } = [];

        /// <summary>True when tokens carry part-of-speech tags.</summary>
        public bool HasPos { get; set; }

        public IEnumerable<TokenInfo> Tokens => Sentences.SelectMany(s => s.Tokens);

        public int WordCount => Sentences.Sum(s => s.WordCount);

        /// <summary>Returns the sentence containing <paramref name="offset"/>, or null.</summary>
        public SentenceInfo? SentenceAt(int offset)
        {
            foreach (var s in Sentences)
                if (offset >= s.Start && offset < s.End) return s;
            return null;
        }

        /// <summary>
        /// 0-based occurrence of this sentence's text among identical sentences earlier in
        /// the scene — lets the editor pick the right one when a line repeats.
        /// </summary>
        public int OccurrenceOf(SentenceInfo sentence)
        {
            int n = 0;
            for (int i = 0; i < sentence.Index; i++)
                if (Sentences[i].Text == sentence.Text) n++;
            return n;
        }
    }

    public sealed class ParagraphInfo
    {
        public int Index { get; init; }
        public int Start { get; init; }
        public int Length { get; init; }
        public int FirstSentenceIndex { get; set; }
        public int SentenceCount { get; set; }
        public ParagraphKind Kind { get; set; }
        public int End => Start + Length;
    }

    public sealed class SentenceInfo
    {
        public int Index { get; init; }
        public int ParagraphIndex { get; init; }
        public int Start { get; init; }
        public string Text { get; init; } = string.Empty;
        public List<TokenInfo> Tokens { get; init; } = [];

        /// <summary>Whole-sentence dialogue classification (same heuristic as the style profile).</summary>
        public bool IsDialogue { get; init; }

        public int Length => Text.Length;
        public int End => Start + Text.Length;

        public IEnumerable<TokenInfo> Words => Tokens.Where(t => t.IsWord);
        public int WordCount => Tokens.Count(t => t.IsWord);
    }

    public sealed class TokenInfo
    {
        /// <summary>Offset in <see cref="SceneDocument.Text"/>.</summary>
        public int Start { get; init; }
        public string Text { get; init; } = string.Empty;
        public int Length => Text.Length;
        public int End => Start + Text.Length;

        /// <summary>Position of this token within its sentence's token list.</summary>
        public int IndexInSentence { get; init; }

        /// <summary>Universal Dependencies coarse tag (VERB, AUX, ADV…), or empty when untagged.</summary>
        public string Pos { get; set; } = string.Empty;

        /// <summary>Normalized word-family key (irregular-form lookup + Porter stem).</summary>
        public string Stem { get; init; } = string.Empty;

        /// <summary>True when the token sits inside quotation marks (spoken text).</summary>
        public bool InQuote { get; init; }

        public bool IsWord { get; init; }

        public string Lower => Text.ToLowerInvariant();

        /// <summary>True for a capitalized word that is not the first word of its sentence.</summary>
        public bool IsCapitalizedMidSentence { get; init; }
    }
}
