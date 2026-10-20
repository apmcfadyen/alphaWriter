using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using alphaWriter.Models;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// Works out each character's pronouns: the Story Bible value when the author set one,
    /// otherwise a guess from the text. The guess looks at sentences that start with the
    /// character's name and counts the he/him/his vs she/her pronouns that follow in the
    /// same sentence ("Maya turned, and her hands…"). It needs several consistent votes.
    /// Guesses are cached per book; call <see cref="Invalidate"/> after big text changes.
    /// </summary>
    public static class PronounInference
    {
        private const int MinVotes = 3;
        private const double MinShare = 0.75;

        private static readonly ConditionalWeakTable<Book, Dictionary<string, PronounSet>> Cache = new();
        private static readonly Regex WordRx = new(@"[\p{L}]+(?:['’][\p{L}]+)?", RegexOptions.Compiled);

        /// <summary>Explicit pronouns if set, otherwise the inferred ones (or Unknown).</summary>
        public static PronounSet For(Character character, Book? book)
        {
            var explicitSet = PronounSets.Parse(character.Pronouns);
            if (explicitSet != PronounSet.Unknown || book is null) return explicitSet;
            return Guesses(book).GetValueOrDefault(character.Id, PronounSet.Unknown);
        }

        /// <summary>The inferred set only (ignores the Story Bible value), for showing as a hint.</summary>
        public static PronounSet Guess(Character character, Book book) =>
            Guesses(book).GetValueOrDefault(character.Id, PronounSet.Unknown);

        public static void Invalidate(Book book) => Cache.Remove(book);

        private static Dictionary<string, PronounSet> Guesses(Book book) =>
            Cache.GetValue(book, Compute);

        private static Dictionary<string, PronounSet> Compute(Book book)
        {
            // First-name word → character (names shared by two characters are skipped)
            var byName = new Dictionary<string, Character>(StringComparer.OrdinalIgnoreCase);
            var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in book.Characters)
            {
                var first = c.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (string.IsNullOrEmpty(first)) continue;
                if (!byName.TryAdd(first, c)) shared.Add(first);
            }
            foreach (var s in shared) byName.Remove(s);

            var votes = new Dictionary<string, (int She, int He)>();
            foreach (var scene in book.Chapters.SelectMany(ch => ch.Scenes))
            {
                if (string.IsNullOrWhiteSpace(scene.Content)) continue;
                var text = NlpTextExtractor.ExtractPlainText(scene.Content);
                foreach (var sentence in NlpTextExtractor.SplitSentences(text))
                {
                    var words = WordRx.Matches(sentence).Select(m => m.Value).ToList();
                    if (words.Count < 3) continue;
                    var first = words[0].Split('\'', '’')[0];
                    if (!byName.TryGetValue(first, out var character)) continue;

                    // Only the clause right after the name: stop at another character's name
                    int she = 0, he = 0;
                    foreach (var w in words.Skip(1))
                    {
                        if (byName.ContainsKey(w)) break;
                        switch (PronounSets.Of(w))
                        {
                            case PronounSet.She: she++; break;
                            case PronounSet.He: he++; break;
                        }
                    }
                    if (she == he) continue;
                    var v = votes.GetValueOrDefault(character.Id);
                    votes[character.Id] = she > he ? (v.She + 1, v.He) : (v.She, v.He + 1);
                }
            }

            var result = new Dictionary<string, PronounSet>();
            foreach (var (id, (she, he)) in votes)
            {
                int total = she + he;
                if (total < MinVotes) continue;
                if (she >= total * MinShare) result[id] = PronounSet.She;
                else if (he >= total * MinShare) result[id] = PronounSet.He;
            }
            return result;
        }
    }
}
