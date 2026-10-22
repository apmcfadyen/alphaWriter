using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// Runs registered rules and finishes their notes: stamps scene identity, attaches
    /// sentence anchors, and computes fingerprints. Rules always run in full — the
    /// author's disabled rules and dismissals are applied when notes are displayed,
    /// so toggling a rule back on never needs a re-run.
    /// </summary>
    public sealed class ProseRuleEngine
    {
        public IReadOnlyList<ISceneRule> SceneRules { get; }
        public IReadOnlyList<IBookRule> BookRules { get; }

        public ProseRuleEngine(IEnumerable<ISceneRule> sceneRules, IEnumerable<IBookRule> bookRules)
        {
            SceneRules = sceneRules.ToList();
            BookRules = bookRules.ToList();
        }

        /// <summary>The built-in rule set.</summary>
        public static ProseRuleEngine CreateDefault() => new(
        [
            new LongSentenceRule(),
            new SceneLengthRule(),
            new NarrationRunRule(),
            new AdverbClusterRule(),
            new AdverbDensityRule(),
            new ShowDontTellRule(),
            new EchoRule(),
            new ClauseStackingRule(),
            new ClauseDensityRule(),
            new PassiveVoiceRule(),
            new OpenerMonotonyRule(),
            new PovSlipRule(),
            new FilterWordsRule(),
            new DialogueTagPunctuationRule(),
            new DialogueTagVerbRule(),
            new DialogueAdverbTagRule(),
            new TalkingHeadsRule(),
            new RedundancyRule(),
            new ClicheRule(),
            new CrutchWordsRule(),
            new NameSpellingRule(),
            new AmbiguousPronounRule(),
            new TenseDriftRule(),
            new GestureRepeatRule(),
            new InfoDumpRule(),
            new SceneGroundingRule(),
            new CharacterPresenceRule(),
            new SensoryBalanceRule(),
            new AbstractPassageRule(),
        ],
        [
            new SceneCountRule(),
            new ChapterLengthRule(),
            new DialogueBalanceRule(),
            new ContractionShiftRule(),
            new SceneOpeningRule(),
            new SceneClosingRule(),
            new FormalityDriftRule(),
            new VocabularyPlateauRule(),
            new ActPacingRule(),
            new EmotionalFlatlineRule(),
            new DialogueDesertRule(),
            new SimilarNamesRule(),
            new GestureTicsBookRule(),
            new ConsistencySheetRule(),
            new SignatureWordsRule(),
            new CharacterAbsenceRule(),
            new AttributeContinuityRule(),
            new SetupPayoffRule(),
            new RepeatedPhraseRule(),
            new ChapterTimeCueRule(),
            new ChapterEndingRule(),
            new TensionSagRule(),
            new ClimaxPlacementRule(),
        ]);

        /// <summary>Tiers that can run for a document: POS rules need tagged tokens.</summary>
        public static bool CanRun(RuleTier tier, bool hasPos) =>
            tier == RuleTier.Lexical || (tier == RuleTier.Pos && hasPos);

        /// <summary>
        /// Evaluates every scene rule whose tier is available (and, if given, is in
        /// <paramref name="tiers"/>), returning finished notes.
        /// </summary>
        public List<NlpNote> EvaluateScene(SceneRuleContext ctx, IReadOnlyCollection<RuleTier>? tiers = null)
        {
            var notes = new List<NlpNote>();
            foreach (var rule in SceneRules)
            {
                var tier = rule.Descriptor.Tier;
                if (!CanRun(tier, ctx.Document.HasPos)) continue;
                if (tiers is not null && !tiers.Contains(tier)) continue;

                foreach (var note in rule.Evaluate(ctx))
                {
                    note.SceneId = ctx.SceneId;
                    note.SceneTitle = ctx.SceneTitle;
                    note.ChapterTitle = ctx.ChapterTitle;
                    NoteAnchoring.Anchor(note, ctx.Document);
                    notes.Add(note);
                }
            }
            return notes;
        }

        public List<NlpNote> EvaluateBook(BookRuleContext ctx)
        {
            var notes = new List<NlpNote>();
            foreach (var rule in BookRules)
            {
                foreach (var note in rule.Evaluate(ctx))
                {
                    NoteAnchoring.EnsureFingerprint(note);
                    notes.Add(note);
                }
            }
            return notes;
        }

        /// <summary>Rule ids produced by scene rules of the given tiers.</summary>
        public HashSet<string> SceneRuleIds(IEnumerable<RuleTier> tiers)
        {
            var set = tiers.ToHashSet();
            return SceneRules.Where(r => set.Contains(r.Descriptor.Tier)).Select(r => r.Descriptor.Id).ToHashSet();
        }
    }
}
