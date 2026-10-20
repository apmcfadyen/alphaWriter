using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// Attaches sentence anchors and fingerprints to notes, and re-locates them after edits.
    /// A note's position is stored two ways: absolute offsets into the scene text, and
    /// (anchor sentence text, occurrence, offset within sentence). The second form survives
    /// edits elsewhere in the scene and is what the editor uses to find the words in its DOM.
    /// </summary>
    public static class NoteAnchoring
    {
        /// <summary>Fills anchors, sentence index and fingerprint from a freshly built document.</summary>
        public static void Anchor(NlpNote note, SceneDocument doc)
        {
            if (note.HasSpan)
            {
                var sentence = doc.SentenceAt(note.Start!.Value);
                if (sentence is not null)
                {
                    note.AnchorText = sentence.Text;
                    note.AnchorOccurrence = doc.OccurrenceOf(sentence);
                    note.AnchorOffset = note.Start.Value - sentence.Start;
                    note.SentenceIndex ??= sentence.Index;
                    if (string.IsNullOrEmpty(note.Evidence))
                        note.Evidence = SafeSubstring(doc.Text, note.Start.Value, note.Length!.Value);
                }
            }
            else if (note.SentenceIndex is int idx && idx >= 0 && idx < doc.Sentences.Count)
            {
                var sentence = doc.Sentences[idx];
                note.AnchorText = sentence.Text;
                note.AnchorOccurrence = doc.OccurrenceOf(sentence);
            }

            foreach (var span in note.ExtraSpans)
            {
                var s = doc.SentenceAt(span.Start);
                if (s is null) continue;
                span.AnchorText = s.Text;
                span.AnchorOccurrence = doc.OccurrenceOf(s);
                span.AnchorOffset = span.Start - s.Start;
            }

            EnsureFingerprint(note);
        }

        /// <summary>
        /// Anchors notes produced outside the rule engine (voice, embeddings, emotions) that
        /// carry only a SentenceIndex, using the scene's persisted sentence list.
        /// </summary>
        public static void AnchorToResults(IEnumerable<NlpNote> notes, IReadOnlyList<SceneAnalysisResult> results)
        {
            var byScene = results.ToDictionary(r => r.SceneId, r => r);
            foreach (var note in notes)
            {
                if (string.IsNullOrEmpty(note.AnchorText)
                    && note.SentenceIndex is int idx
                    && byScene.TryGetValue(note.SceneId, out var result)
                    && idx >= 0 && idx < result.Sentences.Count)
                {
                    var sentence = result.Sentences[idx];
                    note.AnchorText = sentence.Text;
                    note.AnchorOccurrence = result.Sentences.Take(idx).Count(s => s.Text == sentence.Text);
                }
                EnsureFingerprint(note);
            }
        }

        /// <summary>
        /// Moves a note onto a newer version of its scene. Returns false when the anchor
        /// sentence no longer exists (the author rewrote it), in which case the note is stale.
        /// Scene-level notes (no anchor) are kept as-is.
        /// </summary>
        public static bool Reanchor(NlpNote note, SceneDocument doc)
        {
            if (string.IsNullOrEmpty(note.AnchorText)) return true;

            var sentence = FindAnchor(doc, note.AnchorText, note.AnchorOccurrence);
            if (sentence is null) return false;

            note.SentenceIndex = sentence.Index;
            note.AnchorOccurrence = doc.OccurrenceOf(sentence);
            if (note.AnchorOffset is int offset && note.Length is int len)
                note.Start = sentence.Start + offset;
            else if (note.HasSpan)
                note.Start = sentence.Start;

            var kept = new List<TextSpan>();
            foreach (var span in note.ExtraSpans)
            {
                if (string.IsNullOrEmpty(span.AnchorText)) continue;
                var s = FindAnchor(doc, span.AnchorText, span.AnchorOccurrence);
                if (s is null) continue;
                span.Start = s.Start + span.AnchorOffset;
                span.AnchorOccurrence = doc.OccurrenceOf(s);
                kept.Add(span);
            }
            note.ExtraSpans = kept;
            return true;
        }

        private static SentenceInfo? FindAnchor(SceneDocument doc, string anchorText, int occurrence)
        {
            SentenceInfo? first = null;
            int seen = 0;
            foreach (var s in doc.Sentences)
            {
                if (s.Text != anchorText) continue;
                if (seen == occurrence) return s;
                first ??= s;
                seen++;
            }
            // Fewer copies than before (one was deleted) — fall back to the first remaining.
            return first;
        }

        public static void EnsureFingerprint(NlpNote note)
        {
            if (!string.IsNullOrEmpty(note.Fingerprint)) return;
            note.Fingerprint = ComputeFingerprint(note);
        }

        /// <summary>
        /// Rule + scene + evidence + anchor sentence. Deliberately excludes offsets and the
        /// message (which embeds counts), so the fingerprint is stable across unrelated edits.
        /// </summary>
        public static string ComputeFingerprint(NlpNote note)
        {
            var key = string.Join("\u001F",
                note.RuleId,
                note.SceneId,
                string.IsNullOrEmpty(note.SceneId) ? note.ChapterTitle : string.Empty,
                note.Evidence.ToLowerInvariant(),
                note.AnchorText);
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexStringLower(hash, 0, 8);
        }

        private static string SafeSubstring(string s, int start, int length)
        {
            if (start < 0 || start >= s.Length) return string.Empty;
            return s.Substring(start, Math.Min(length, s.Length - start));
        }
    }
}
