using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Parsing;
using static alphaWriter.Services.Nlp.Rules.RuleHelpers;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// A copyeditor's style sheet, built automatically: choices the book makes more than one
    /// way (colour / color, ice-cold / ice cold, — / --, … / ...). The majority choice wins;
    /// each minority use gets a one-click fix, and one summary note lists every decision.
    /// </summary>
    public sealed class ConsistencySheetRule : IBookRule
    {
        public RuleDescriptor Descriptor => RuleCatalog.ConsistencySheet;

        /// <summary>One occurrence of a variant: where it is and how it is written.</summary>
        private sealed record Use(SceneAnalysisResult Result, int SentenceIndex, int Offset, string Text);

        public IEnumerable<NlpNote> Evaluate(BookRuleContext ctx)
        {
            var decisions = new List<string>();
            var notes = new List<NlpNote>();

            // ── US / UK spelling ────────────────────────────────────────────
            var spellings = new Dictionary<string, (string Family, bool Uk, string Counterpart)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (family, pairs) in SpellingFamilies)
                foreach (var (us, uk) in pairs)
                {
                    spellings[us] = (family, false, uk);
                    spellings[uk] = (family, true, us);
                }

            var spellingUses = new Dictionary<string, List<(Use Use, bool Uk, string Counterpart)>>();
            foreach (var (result, sentence, i) in Sentences(ctx))
                foreach (Match m in WordRx.Matches(sentence.Text))
                    if (spellings.TryGetValue(m.Value, out var info))
                    {
                        if (!spellingUses.TryGetValue(info.Family, out var list)) spellingUses[info.Family] = list = [];
                        list.Add((new Use(result, i, m.Index, m.Value), info.Uk, info.Counterpart));
                    }

            foreach (var (family, uses) in spellingUses)
            {
                int uk = uses.Count(u => u.Uk), us = uses.Count - uk;
                if (uk == 0 || us == 0) continue;
                bool ukWins = uk >= us;
                decisions.Add($"{family}: {(ukWins ? "UK" : "US")} spelling ({Math.Max(uk, us)} ×) over {(ukWins ? "US" : "UK")} ({Math.Min(uk, us)} ×)");
                foreach (var (use, isUk, counterpart) in uses.Where(u => u.Uk != ukWins))
                    notes.Add(Fix(use, MatchCase(counterpart, use.Text),
                        $"“{use.Text}” — elsewhere the book uses {(ukWins ? "UK" : "US")} spelling " +
                        $"(“{MatchCase(counterpart, use.Text)}”). Pick one and keep it."));
            }

            // ── Hyphenation: ice-cold / ice cold / icecold ───────────────────
            var compounds = new Dictionary<string, List<(Use Use, string Form)>>(StringComparer.OrdinalIgnoreCase);
            var hyphenated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, sentence, _) in Sentences(ctx))
                foreach (Match m in HyphenRx.Matches(sentence.Text))
                    hyphenated.Add(m.Value.ToLowerInvariant());

            if (hyphenated.Count > 0)
            {
                // One regex per compound and form, compiled once for the whole book
                var compoundPatterns = hyphenated.Select(key =>
                {
                    var parts = key.Split('-');
                    var (a, b) = (Regex.Escape(parts[0]), Regex.Escape(parts[1]));
                    return (Key: key, Patterns: new (string Form, Regex Rx)[]
                    {
                        ("hyphenated", new Regex($@"\b{a}-{b}\b", RegexOptions.IgnoreCase)),
                        ("open", new Regex($@"\b{a} {b}\b", RegexOptions.IgnoreCase)),
                        ("closed", new Regex($@"\b{a}{b}\b", RegexOptions.IgnoreCase)),
                    });
                }).ToList();

                foreach (var (result, sentence, i) in Sentences(ctx))
                {
                    foreach (var (key, patterns) in compoundPatterns)
                    {
                        foreach (var (form, rx) in patterns)
                            foreach (Match m in rx.Matches(sentence.Text))
                            {
                                if (!compounds.TryGetValue(key, out var list)) compounds[key] = list = [];
                                list.Add((new Use(result, i, m.Index, m.Value), form));
                            }
                    }
                }

                foreach (var (key, uses) in compounds)
                {
                    var forms = uses.GroupBy(u => u.Form).OrderByDescending(g => g.Count()).ToList();
                    if (forms.Count < 2) continue;
                    var parts = key.Split('-');
                    var winner = forms[0].Key;
                    string Render(string form, string like) => MatchCase(form switch
                    {
                        "open" => $"{parts[0]} {parts[1]}",
                        "closed" => parts[0] + parts[1],
                        _ => key
                    }, like);
                    decisions.Add($"“{Render(winner, key)}” ({forms[0].Count()} ×) over " +
                                  string.Join(", ", forms.Skip(1).Select(f => $"“{Render(f.Key, key)}” ({f.Count()} ×)")));
                    foreach (var (use, _) in uses.Where(u => u.Form != winner))
                        notes.Add(Fix(use, Render(winner, use.Text),
                            $"“{use.Text}” is written as “{Render(winner, use.Text)}” everywhere else."));
                }
            }

            // ── Dashes and ellipses ─────────────────────────────────────────
            AddPunctuation(ctx, DashStyles, "Dashes", decisions, notes);
            AddPunctuation(ctx, EllipsisStyles, "Ellipses", decisions, notes);

            if (decisions.Count > 0)
            {
                notes.Insert(0, Note(Descriptor, NlpNoteSeverity.Info,
                    "Style sheet for this book, from what you use most: " + string.Join("; ", decisions) +
                    ". Each exception is flagged in its scene with a one-click fix."));
            }
            foreach (var n in notes) yield return n;
        }

        private void AddPunctuation(BookRuleContext ctx, (string Name, Regex Rx, string Canonical)[] styles,
            string label, List<string> decisions, List<NlpNote> notes)
        {
            var uses = new List<(Use Use, int Style)>();
            foreach (var (result, sentence, i) in Sentences(ctx))
                for (int s = 0; s < styles.Length; s++)
                    foreach (Match m in styles[s].Rx.Matches(sentence.Text))
                        uses.Add((new Use(result, i, m.Index, m.Value), s));

            var byStyle = uses.GroupBy(u => u.Style).OrderByDescending(g => g.Count()).ToList();
            if (byStyle.Count < 2) return;
            var winner = styles[byStyle[0].Key];
            decisions.Add($"{label}: {winner.Name} ({byStyle[0].Count()} ×) over " +
                          string.Join(", ", byStyle.Skip(1).Select(g => $"{styles[g.Key].Name} ({g.Count()} ×)")));
            foreach (var (use, style) in uses.Where(u => u.Style != byStyle[0].Key))
                notes.Add(Fix(use, winner.Canonical,
                    $"This {styles[style].Name} — the rest of the book uses {winner.Name} (“{winner.Canonical.Trim()}”)."));
        }

        private NlpNote Fix(Use use, string replacement, string message)
        {
            var note = Note(Descriptor, NlpNoteSeverity.Info, message)
                .ForScene(use.Result)
                .AtSentence(use.Result, use.SentenceIndex);
            note.Start += use.Offset;
            note.Length = use.Text.Length;
            note.AnchorOffset = use.Offset;
            note.Evidence = use.Text;
            note.Suggestions.Add(replacement);
            return note;
        }

        private static IEnumerable<(SceneAnalysisResult Result, SentenceAnalysis Sentence, int Index)> Sentences(BookRuleContext ctx)
        {
            foreach (var r in ctx.Results)
                for (int i = 0; i < r.Sentences.Count; i++)
                    yield return (r, r.Sentences[i], i);
        }

        private static string MatchCase(string replacement, string original) =>
            original.Length > 0 && char.IsUpper(original[0]) && replacement.Length > 0
                ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
                : replacement;

        private static readonly Regex WordRx = new(@"\b[\p{L}]+\b", RegexOptions.Compiled);
        private static readonly Regex HyphenRx = new(@"\b[\p{L}]{2,}-[\p{L}]{2,}\b", RegexOptions.Compiled);

        private static readonly (string Name, Regex Rx, string Canonical)[] DashStyles =
        [
            ("em dashes", new Regex("—", RegexOptions.Compiled), "—"),
            ("double hyphens", new Regex("(?<!-)--(?!-)", RegexOptions.Compiled), "--"),
            ("spaced hyphens", new Regex("(?<=\\S) - (?=\\S)", RegexOptions.Compiled), " - "),
        ];

        private static readonly (string Name, Regex Rx, string Canonical)[] EllipsisStyles =
        [
            ("ellipsis characters", new Regex("…", RegexOptions.Compiled), "…"),
            ("three dots", new Regex(@"(?<!\.)\.\.\.(?!\.)", RegexOptions.Compiled), "..."),
            ("spaced dots", new Regex(@"\. \. \.", RegexOptions.Compiled), ". . ."),
        ];

        /// <summary>US / UK spelling families: family name → (US form, UK form) for each inflection.</summary>
        private static readonly (string Family, (string Us, string Uk)[] Pairs)[] SpellingFamilies =
        [
            ("colour", [("color", "colour"), ("colors", "colours"), ("colored", "coloured"), ("colorful", "colourful"), ("colorless", "colourless")]),
            ("grey", [("gray", "grey"), ("grays", "greys"), ("grayish", "greyish")]),
            ("favourite", [("favorite", "favourite"), ("favorites", "favourites")]),
            ("centre", [("center", "centre"), ("centers", "centres"), ("centered", "centred")]),
            ("theatre", [("theater", "theatre"), ("theaters", "theatres")]),
            ("travelled", [("traveled", "travelled"), ("traveling", "travelling"), ("traveler", "traveller"), ("travelers", "travellers")]),
            ("harbour", [("harbor", "harbour"), ("harbors", "harbours")]),
            ("honour", [("honor", "honour"), ("honored", "honoured"), ("honors", "honours"), ("honorable", "honourable")]),
            ("neighbour", [("neighbor", "neighbour"), ("neighbors", "neighbours"), ("neighborhood", "neighbourhood")]),
            ("rumour", [("rumor", "rumour"), ("rumors", "rumours")]),
            ("armour", [("armor", "armour"), ("armored", "armoured")]),
            ("odour", [("odor", "odour"), ("odors", "odours")]),
            ("labour", [("labor", "labour"), ("labored", "laboured")]),
            ("vapour", [("vapor", "vapour")]),
            ("parlour", [("parlor", "parlour")]),
            ("saviour", [("savior", "saviour")]),
            ("mould", [("mold", "mould"), ("moldy", "mouldy")]),
            ("defence", [("defense", "defence")]),
            ("catalogue", [("catalog", "catalogue")]),
            ("realise", [("realize", "realise"), ("realized", "realised"), ("realizes", "realises"), ("realizing", "realising")]),
            ("recognise", [("recognize", "recognise"), ("recognized", "recognised"), ("recognizing", "recognising")]),
            ("apologise", [("apologize", "apologise"), ("apologized", "apologised"), ("apologizing", "apologising")]),
            ("organise", [("organize", "organise"), ("organized", "organised"), ("organizing", "organising")]),
            ("analyse", [("analyze", "analyse"), ("analyzed", "analysed")]),
            ("towards", [("toward", "towards")]),
            ("amongst", [("among", "amongst")]),
            ("amidst", [("amid", "amidst")]),
            ("cosy", [("cozy", "cosy")]),
            ("pyjamas", [("pajamas", "pyjamas")]),
            ("sceptical", [("skeptical", "sceptical")]),
            ("jewellery", [("jewelry", "jewellery")]),
            ("plough", [("plow", "plough"), ("plowed", "ploughed")]),
            ("moustache", [("mustache", "moustache")]),
            ("aluminium", [("aluminum", "aluminium")]),
        ];
    }
}
