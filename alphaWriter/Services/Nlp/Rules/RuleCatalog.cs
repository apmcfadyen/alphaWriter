using System.Collections.Generic;
using System.Linq;
using alphaWriter.Models.Analysis;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>
    /// Every rule the editing suite can report, including those implemented outside the
    /// rule engine (embedding, emotion and voice analysis). The settings panel lists these.
    /// </summary>
    public static class RuleCatalog
    {
        /// <summary>
        /// Bump when scene-rule output changes, so cached per-scene results are recomputed
        /// instead of reused.
        /// </summary>
        public const int EngineVersion = 1;

        private const NlpNoteCategory Copy = NlpNoteCategory.CopyEditor;
        private const NlpNoteCategory Line = NlpNoteCategory.LineEditor;
        private const NlpNoteCategory Dev = NlpNoteCategory.DevelopmentalEditor;

        // ── Scene rules (rule engine) ───────────────────────────────────────

        public static readonly RuleDescriptor LongSentence = new(
            "sentence.long", "Unusually long sentences",
            "Sentences far longer than the scene's norm, where readers may lose the thread.",
            Dev, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor SceneLength = new(
            "pacing.scene-length", "Scene length outliers",
            "Scenes much longer or shorter than the rest of their chapter.",
            Dev, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor NarrationRun = new(
            "pacing.narration-run", "Long stretches without dialogue",
            "Long unbroken runs of narration that can feel airless.",
            Dev, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor Adverbs = new(
            "style.adverbs", "Adverb clusters",
            "Sentences leaning on several -ly adverbs where a stronger verb would do.",
            Copy, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor AdverbDensity = new(
            "style.adverb-density", "Adverb density",
            "Scenes where -ly adverbs pad the prose overall.",
            Copy, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor ShowDontTell = new(
            "style.show-dont-tell", "Named emotions",
            "Narration that labels a feeling (\"she felt angry\") instead of showing it.",
            Line, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor Echo = new(
            "style.echo", "Word echoes",
            "The same word family repeated within a few sentences.",
            Copy, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor ClauseStacking = new(
            "style.clause-stacking", "Stacked clauses",
            "Sentences with many subordinate clauses that bury the main idea.",
            Line, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor ClauseDensity = new(
            "style.clause-density", "Clause density",
            "Scenes whose sentences are consistently heavy with subordinate clauses.",
            Line, RuleTier.Lexical, RuleScope.Scene);

        public static readonly RuleDescriptor Passive = new(
            "style.passive", "Passive voice",
            "Passive constructions that hide or delay who is acting.",
            Copy, RuleTier.Pos, RuleScope.Scene);

        public static readonly RuleDescriptor OpenerMonotony = new(
            "style.opener-monotony", "Repetitive sentence openers",
            "Scenes where most sentences open with the same kind of word.",
            Line, RuleTier.Pos, RuleScope.Scene);

        // ── Chapter comparisons ─────────────────────────────────────────────

        public static readonly RuleDescriptor SentenceLengthDrift = new(
            "style.sentence-length-drift", "Sentence length vs chapter",
            "Scenes whose average sentence length departs from their chapter.",
            Copy, RuleTier.Lexical, RuleScope.Chapter);

        public static readonly RuleDescriptor VocabularyDrift = new(
            "style.vocabulary-drift", "Vocabulary variety vs chapter",
            "Scenes noticeably more repetitive or more varied than their chapter.",
            Dev, RuleTier.Lexical, RuleScope.Chapter);

        public static readonly RuleDescriptor ReadabilityDrift = new(
            "style.readability-drift", "Readability vs chapter",
            "Scenes much harder or easier to read than their chapter.",
            Dev, RuleTier.Lexical, RuleScope.Chapter);

        public static readonly RuleDescriptor SceneOutlier = new(
            "semantic.scene-outlier", "Off-topic scenes",
            "Scenes whose subject matter differs sharply from the rest of the chapter (embedding model).",
            Dev, RuleTier.Model, RuleScope.Chapter);

        public static readonly RuleDescriptor TopicDrift = new(
            "semantic.topic-drift", "Focus shifts within a scene",
            "Points where a scene's focus changes abruptly (embedding model).",
            Dev, RuleTier.Model, RuleScope.Scene);

        public static readonly RuleDescriptor ToneShift = new(
            "emotion.tone-shift", "Abrupt tone shifts",
            "Sudden changes in emotional register within a scene (emotion model).",
            Line, RuleTier.Model, RuleScope.Scene);

        // ── Book comparisons ────────────────────────────────────────────────

        public static readonly RuleDescriptor SceneCount = new(
            "structure.scene-count", "Scene count per chapter",
            "Chapters with far more or fewer scenes than the book's norm.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor ChapterLength = new(
            "structure.chapter-length", "Chapter length outliers",
            "Chapters far longer or shorter than the book average.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor DialogueBalance = new(
            "structure.dialogue-balance", "Dialogue balance",
            "Scenes unusually heavy or light on dialogue compared with the book.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor ContractionShift = new(
            "voice.contraction-shift", "Register shifts",
            "Scenes noticeably more formal or casual than the rest of the book.",
            Line, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor SceneOpening = new(
            "structure.scene-opening", "Heavy scene openings",
            "Scenes that open with a long, complex sentence.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor SceneClosing = new(
            "structure.scene-closing", "Diffuse scene endings",
            "Scenes that close on a long sentence instead of a crisp final beat.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor FormalityDrift = new(
            "voice.formality-drift", "Formality drift across the book",
            "Prose growing steadily more formal or casual from first half to second.",
            Line, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor VocabularyPlateau = new(
            "style.vocabulary-plateau", "Vocabulary plateau",
            "A second half that introduces almost no new vocabulary.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor ActPacing = new(
            "structure.act-pacing", "Sagging middle",
            "Middle-act scenes running much longer than Act 1 scenes.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor EmotionalFlatline = new(
            "emotion.flatline", "Emotional flatline",
            "Chapters that stay in one or two emotional tones throughout (emotion model).",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor DialogueDesert = new(
            "structure.dialogue-desert", "Dialogue deserts",
            "Runs of several consecutive scenes with almost no dialogue.",
            Dev, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor CharacterVoice = new(
            "voice.character", "Character voice consistency",
            "Viewpoint characters whose narration drifts from their usual voice.",
            Line, RuleTier.Book, RuleScope.Book);

        public static readonly RuleDescriptor DialogueVoice = new(
            "voice.dialogue", "Distinct dialogue voices",
            "Characters whose dialogue is hard to tell apart.",
            Line, RuleTier.Book, RuleScope.Book);

        public static IReadOnlyList<RuleDescriptor> All { get; } =
        [
            LongSentence, SceneLength, NarrationRun, Adverbs, AdverbDensity, ShowDontTell, Echo,
            ClauseStacking, ClauseDensity, Passive, OpenerMonotony,
            SentenceLengthDrift, VocabularyDrift, ReadabilityDrift, SceneOutlier, TopicDrift, ToneShift,
            SceneCount, ChapterLength, DialogueBalance, ContractionShift, SceneOpening, SceneClosing,
            FormalityDrift, VocabularyPlateau, ActPacing, EmotionalFlatline, DialogueDesert,
            CharacterVoice, DialogueVoice
        ];

        private static readonly Dictionary<string, RuleDescriptor> ById = All.ToDictionary(r => r.Id);

        public static RuleDescriptor? Find(string ruleId) =>
            ruleId is not null && ById.TryGetValue(ruleId, out var d) ? d : null;
    }
}
