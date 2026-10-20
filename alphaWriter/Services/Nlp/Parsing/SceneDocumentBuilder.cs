using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace alphaWriter.Services.Nlp.Parsing
{
    /// <summary>
    /// Builds a <see cref="SceneDocument"/> from scene HTML (or plain text): one pass for
    /// paragraphs, sentences and tokens, plus an optional single POS-tagging pass.
    /// </summary>
    public static class SceneDocumentBuilder
    {
        // Words (with internal apostrophes / hyphens) or single non-space characters.
        private static readonly Regex TokenRx = new(
            @"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*|\S",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRx = new(@"[\s ]+", RegexOptions.Compiled);

        public static SceneDocument FromHtml(string html, IPosTaggingService? pos = null) =>
            FromPlainText(NlpTextExtractor.ExtractPlainText(html ?? string.Empty), pos);

        /// <summary>Builds from plain text where '\n' separates paragraphs.</summary>
        public static SceneDocument FromPlainText(string plainText, IPosTaggingService? pos = null)
        {
            var paragraphTexts = (plainText ?? string.Empty)
                .Split('\n')
                .Select(p => WhitespaceRx.Replace(p, " ").Trim())
                .Where(p => p.Length > 0)
                .ToList();
            return Build(paragraphTexts, pos);
        }

        /// <summary>
        /// Builds from a pre-split sentence list, treating each sentence as its own paragraph.
        /// Used by the legacy sentence-list APIs and by tests.
        /// </summary>
        public static SceneDocument FromSentences(IReadOnlyList<string> sentences) =>
            Build(sentences.Select(s => WhitespaceRx.Replace(s ?? string.Empty, " ").Trim()).ToList(),
                  pos: null, splitSentences: false);

        private static SceneDocument Build(List<string> paragraphTexts, IPosTaggingService? pos,
            bool splitSentences = true)
        {
            var text = new StringBuilder();
            var paragraphs = new List<ParagraphInfo>();
            var sentences = new List<SentenceInfo>();

            for (int p = 0; p < paragraphTexts.Count; p++)
            {
                var para = paragraphTexts[p];
                if (p > 0) text.Append('\n');
                int paraStart = text.Length;
                text.Append(para);

                var info = new ParagraphInfo
                {
                    Index = paragraphs.Count,
                    Start = paraStart,
                    Length = para.Length,
                    FirstSentenceIndex = sentences.Count
                };
                paragraphs.Add(info);

                var spans = splitSentences
                    ? SentenceSplitter.Split(para)
                    : (para.Length > 0 ? [(0, para.Length)] : new List<(int, int)>());

                bool inQuote = false; // quote state carries across sentences within a paragraph
                int quotedWords = 0, words = 0;

                foreach (var (start, length) in spans)
                {
                    var sText = para.Substring(start, length);
                    int absStart = paraStart + start;
                    var tokens = Tokenize(sText, absStart, ref inQuote);
                    foreach (var t in tokens)
                    {
                        if (!t.IsWord) continue;
                        words++;
                        if (t.InQuote) quotedWords++;
                    }

                    sentences.Add(new SentenceInfo
                    {
                        Index = sentences.Count,
                        ParagraphIndex = info.Index,
                        Start = absStart,
                        Text = sText,
                        Tokens = tokens,
                        IsDialogue = NlpTextExtractor.IsDialogue(sText)
                    });
                }

                info.SentenceCount = sentences.Count - info.FirstSentenceIndex;
                info.Kind = quotedWords == 0 ? ParagraphKind.Narration
                    : quotedWords >= words * 0.9 ? ParagraphKind.Dialogue
                    : ParagraphKind.Mixed;
            }

            var doc = new SceneDocument
            {
                Text = text.ToString(),
                Paragraphs = paragraphs,
                Sentences = sentences
            };

            if (pos is not null && pos.IsLoaded)
                ApplyPos(doc, pos);

            return doc;
        }

        private static List<TokenInfo> Tokenize(string sentence, int absStart, ref bool inQuote)
        {
            var tokens = new List<TokenInfo>();
            bool firstWordSeen = false;

            foreach (Match m in TokenRx.Matches(sentence))
            {
                var value = m.Value;
                bool isWord = char.IsLetterOrDigit(value[0]);

                if (!isWord)
                {
                    // Quote tracking: straight quotes toggle, curly quotes open/close.
                    // Apostrophes (’ / ') are ignored — too ambiguous with contractions.
                    switch (value[0])
                    {
                        case '"': inQuote = !inQuote; break;
                        case '“': inQuote = true; break;
                        case '”': inQuote = false; break;
                    }
                }

                bool capitalizedMid = isWord && firstWordSeen && char.IsUpper(value[0]);
                if (isWord) firstWordSeen = true;

                tokens.Add(new TokenInfo
                {
                    Start = absStart + m.Index,
                    Text = value,
                    IndexInSentence = tokens.Count,
                    IsWord = isWord,
                    InQuote = isWord && inQuote,
                    Stem = isWord ? WordStemmer.Stem(value) : string.Empty,
                    IsCapitalizedMidSentence = capitalizedMid
                });
            }

            return tokens;
        }

        // ── POS tagging ─────────────────────────────────────────────────────

        /// <summary>
        /// Tags every sentence in one call and copies each tagger token's POS onto the
        /// document token it starts inside. Tokenizations differ ("don't" → do + n't), so
        /// alignment is by character offset, not by token index.
        /// </summary>
        public static void ApplyPos(SceneDocument doc, IPosTaggingService pos)
        {
            if (doc.Sentences.Count == 0) return;

            IReadOnlyList<(int Start, int Length, string Pos)>[] tagged;
            try
            {
                tagged = pos.TagSpans(doc.Sentences.Select(s => s.Text).ToList());
            }
            catch (InvalidOperationException)
            {
                return; // tagger not loaded
            }
            if (tagged is null || tagged.Length != doc.Sentences.Count) return;

            for (int i = 0; i < doc.Sentences.Count; i++)
            {
                var sentence = doc.Sentences[i];
                var spans = tagged[i];
                if (spans is null) continue;

                foreach (var token in sentence.Tokens)
                {
                    int rel = token.Start - sentence.Start;
                    // First tagger span that starts at or inside this token
                    foreach (var (start, length, tag) in spans)
                    {
                        if (start >= rel && start < rel + token.Length || rel >= start && rel < start + length)
                        {
                            token.Pos = tag;
                            break;
                        }
                    }
                }
            }

            doc.HasPos = true;
        }

        /// <summary>
        /// Applies legacy (value, POS) tuples — the shape <see cref="IPosTaggingService.TagSentences"/>
        /// returns — by matching token text in order. Used by the sentence-list compatibility APIs.
        /// </summary>
        public static void ApplyLegacyTags(SceneDocument doc,
            IReadOnlyList<(string Value, string Pos)>[] tagged)
        {
            if (tagged is null || tagged.Length != doc.Sentences.Count) return;

            for (int i = 0; i < doc.Sentences.Count; i++)
            {
                var tags = tagged[i];
                if (tags is null) continue;
                int j = 0;
                foreach (var token in doc.Sentences[i].Tokens)
                {
                    for (int k = j; k < tags.Count && k < j + 4; k++)
                    {
                        if (string.Equals(tags[k].Value, token.Text, StringComparison.OrdinalIgnoreCase))
                        {
                            token.Pos = tags[k].Pos;
                            j = k + 1;
                            break;
                        }
                    }
                }
            }

            doc.HasPos = true;
        }
    }

    /// <summary>
    /// Sentence boundary detection over a single paragraph. Splits after terminal
    /// punctuation (optionally followed by closing quotes/brackets) when the next
    /// sentence starts with a capital, digit or opening quote. Merges fragments that
    /// ended on a known abbreviation ("Dr.", "Mr.").
    /// </summary>
    public static class SentenceSplitter
    {
        private static readonly Regex BoundaryRx = new(
            "(?<=[.!?…][\"'”’)\\]]*)\\s+(?=[\"'“‘(\\[]*[\\p{Lu}\\p{N}])",
            RegexOptions.Compiled);

        private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "ave", "blvd",
            "dept", "est", "vol", "vs", "etc", "inc", "ltd", "corp", "co",
            "gen", "gov", "sgt", "cpl", "pvt", "capt", "lt", "col", "cmdr",
            "adm", "rev", "hon", "pres", "sec", "treas", "amb"
        };

        /// <summary>Returns (start, length) spans of trimmed sentences within <paramref name="paragraph"/>.</summary>
        public static List<(int Start, int Length)> Split(string paragraph)
        {
            var result = new List<(int Start, int Length)>();
            if (string.IsNullOrWhiteSpace(paragraph)) return result;

            int segStart = 0;
            foreach (Match m in BoundaryRx.Matches(paragraph))
            {
                AddSegment(paragraph, segStart, m.Index, result);
                segStart = m.Index + m.Length;
            }
            AddSegment(paragraph, segStart, paragraph.Length, result);
            return result;
        }

        private static void AddSegment(string text, int start, int end,
            List<(int Start, int Length)> result)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end <= start) return;

            if (result.Count > 0)
            {
                var (pStart, pLen) = result[^1];
                if (EndsWithAbbreviation(text.AsSpan(pStart, pLen)))
                {
                    result[^1] = (pStart, end - pStart);
                    return;
                }
            }
            result.Add((start, end - start));
        }

        private static bool EndsWithAbbreviation(ReadOnlySpan<char> s)
        {
            if (s.Length < 2 || s[^1] != '.') return false;
            int end = s.Length - 1;
            int start = end;
            while (start > 0 && char.IsLetter(s[start - 1])) start--;
            if (start == end) return false;
            return Abbreviations.Contains(s[start..end].ToString());
        }
    }
}
