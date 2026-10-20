using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Services.Nlp.Parsing;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>Word lists and phrase tables for the line- and copy-editing rules.</summary>
    internal static class LineLexicons
    {
        private static HashSet<string> Stems(params string[] words) =>
            words.Select(WordStemmer.Stem).ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> Set(params string[] words) =>
            new(words, StringComparer.OrdinalIgnoreCase);

        // ── Viewpoint ───────────────────────────────────────────────────────

        /// <summary>Verbs that report what a character privately thinks or feels.</summary>
        public static readonly HashSet<string> InteriorVerbStems = Stems(
            "thought", "knew", "wondered", "felt", "wanted", "realized", "realised", "hoped",
            "feared", "decided", "remembered", "worried", "believed", "suspected", "sensed",
            "longed", "wished", "regretted", "understood", "doubted", "imagined", "resented");

        /// <summary>
        /// Perception and cognition verbs that put a filter between reader and scene
        /// ("she saw the door open" vs "the door opened").
        /// </summary>
        public static readonly HashSet<string> FilterVerbStems = Stems(
            "saw", "heard", "felt", "noticed", "realized", "realised", "watched", "wondered",
            "observed", "smelled", "smelt", "sensed", "seemed", "noted", "glimpsed");

        // ── Dialogue ────────────────────────────────────────────────────────

        /// <summary>Verbs that can carry speech in a dialogue tag.</summary>
        public static readonly HashSet<string> SpeechVerbStems = Stems(
            "said", "asked", "replied", "whispered", "shouted", "muttered", "murmured", "called",
            "cried", "yelled", "answered", "added", "continued", "insisted", "suggested",
            "explained", "told", "repeated", "agreed", "admitted", "demanded", "warned");

        /// <summary>Showy alternatives to "said" that pull attention onto the tag.</summary>
        public static readonly HashSet<string> SaidBookisms = Set(
            "exclaimed", "exclaims", "retorted", "retorts", "queried", "queries", "inquired",
            "inquires", "enquired", "interjected", "interjects", "opined", "opines",
            "expostulated", "ejaculated", "chortled", "uttered", "utters", "declared",
            "proclaimed", "proclaims", "intoned", "quipped", "quips", "gushed", "enthused",
            "breathed", "vociferated", "articulated", "elucidated", "averred", "ventured");

        /// <summary>Actions that cannot produce speech, used as if they were tags ("she smiled").</summary>
        public static readonly HashSet<string> ActionNotSpeechStems = Stems(
            "smiled", "grinned", "laughed", "sighed", "shrugged", "nodded", "smirked", "frowned",
            "chuckled", "giggled", "snorted", "beamed", "winked", "scowled", "grimaced");

        public static readonly HashSet<string> TagPronouns = Set("he", "she", "they", "i", "we", "you", "it");

        // ── Crutch words ────────────────────────────────────────────────────

        public static readonly IReadOnlyList<(PhrasePattern Pattern, string Label)> CrutchWords =
        [
            (new("just"), "just"), (new("really"), "really"), (new("very"), "very"),
            (new("suddenly"), "suddenly"), (new("somehow"), "somehow"), (new("actually"), "actually"),
            (new("quite"), "quite"), (new("rather"), "rather"), (new("simply"), "simply"),
            (new("literally"), "literally"), (new("basically"), "basically"), (new("totally"), "totally"),
            (new("~begin", "to"), "began to"), (new("~start", "to"), "started to"),
            (new("~seem", "to"), "seemed to"), (new("a", "bit"), "a bit"), (new("a", "little"), "a little"),
            (new("sort", "of"), "sort of"), (new("kind", "of"), "kind of"),
        ];

        // ── Redundancies: (pattern, replacement) ────────────────────────────
        // Replacement "$n" keeps the n-th matched word as written (keeps its tense).

        public static readonly IReadOnlyList<(PhrasePattern Pattern, string Replacement)> Redundancies =
        [
            (new("~nod", "POSS", "head"), "$0"),
            (new("~shrug", "POSS", "shoulders"), "$0"),
            (new("~blink", "POSS", "eyes"), "$0"),
            (new("~think", "to", "REFL"), "$0"),
            (new("~mutter", "under", "POSS", "breath"), "$0"),
            (new("~whisper", "quietly"), "$0"),
            (new("~whisper", "softly"), "$0"),
            (new("~shout", "loudly"), "$0"),
            (new("~yell", "loudly"), "$0"),
            (new("~scream", "loudly"), "$0"),
            (new("~join", "together"), "$0"),
            (new("~return", "back"), "$0"),
            (new("~revert", "back"), "$0"),
            (new("~reply", "back"), "$0"),
            (new("~repeat", "again"), "$0"),
            (new("still", "~remain"), "$1"),
            (new("completely", "~finish"), "$1"),
            (new("each", "and", "every"), "every"),
            (new("end", "result"), "result"),
            (new("final", "outcome"), "outcome"),
            (new("past", "history"), "history"),
            (new("free", "gift"), "gift"),
            (new("unexpected", "surprise"), "surprise"),
            (new("close", "proximity"), "proximity"),
            (new("brief", "moment"), "moment"),
            (new("absolutely", "essential"), "essential"),
            (new("the", "reason", "why"), "the reason"),
        ];

        // ── Weak verb + noun: (pattern, verb forms of the stronger verb) ────

        private static VerbForms V(string b, string past, string s, string ing) => new(b, past, s, ing);

        public static readonly IReadOnlyList<(PhrasePattern Pattern, VerbForms Better)> WeakVerbPhrases =
        [
            (new("~make", "DET", "decision"), V("decide", "decided", "decides", "deciding")),
            (new("~make", "DET", "attempt"), V("attempt", "attempted", "attempts", "attempting")),
            (new("~make", "DET", "suggestion"), V("suggest", "suggested", "suggests", "suggesting")),
            (new("~make", "DET", "assumption"), V("assume", "assumed", "assumes", "assuming")),
            (new("~give", "DET", "smile"), V("smile", "smiled", "smiles", "smiling")),
            (new("~give", "DET", "nod"), V("nod", "nodded", "nods", "nodding")),
            (new("~give", "DET", "shrug"), V("shrug", "shrugged", "shrugs", "shrugging")),
            (new("~give", "DET", "sigh"), V("sigh", "sighed", "sighs", "sighing")),
            (new("~give", "DET", "laugh"), V("laugh", "laughed", "laughs", "laughing")),
            (new("~let", "out", "DET", "sigh"), V("sigh", "sighed", "sighs", "sighing")),
            (new("~let", "out", "DET", "scream"), V("scream", "screamed", "screams", "screaming")),
            (new("~let", "out", "DET", "laugh"), V("laugh", "laughed", "laughs", "laughing")),
            (new("~take", "DET", "look"), V("look", "looked", "looks", "looking")),
            (new("~come", "to", "DET", "stop"), V("stop", "stopped", "stops", "stopping")),
            (new("~come", "to", "DET", "conclusion"), V("conclude", "concluded", "concludes", "concluding")),
            (new("~have", "DET", "discussion"), V("discuss", "discussed", "discusses", "discussing")),
        ];

        // ── Clichés (no automatic fix — the point is to find the specific version) ──

        public static readonly IReadOnlyList<PhrasePattern> Cliches =
        [
            new("POSS", "heart", "~skip", "DET", "beat"),
            new("POSS", "heart", "~pound", "in", "POSS", "chest"),
            new("POSS", "blood", "~run", "cold"),
            new("time", "~stand", "still"),
            new("dead", "of", "night"),
            new("crack", "of", "dawn"),
            new("silence", "was", "deafening"),
            new("deafening", "silence"),
            new("butterflies", "in", "POSS", "stomach"),
            new("DET", "chill", "~run", "down", "POSS", "spine"),
            new("~shiver", "down", "POSS", "spine"),
            new("~send", "shivers", "down", "POSS", "spine"),
            new("in", "the", "nick", "of", "time"),
            new("at", "the", "end", "of", "the", "day"),
            new("only", "time", "will", "tell"),
            new("all", "hell", "broke", "loose"),
            new("calm", "before", "the", "storm"),
            new("white", "as", "a", "sheet"),
            new("cold", "as", "ice"),
            new("quiet", "as", "a", "mouse"),
            new("little", "did", "SUBJ", "know"),
            new("every", "fiber", "of", "POSS", "being"),
            new("every", "fibre", "of", "POSS", "being"),
            new("POSS", "breath", "~catch", "in", "POSS", "throat"),
            new("DET", "wave", "of", "relief", "~wash", "over"),
            new("breath", "SUBJ", "didn't", "know"),
            new("~smile", "that", "didn't", "reach", "POSS", "eyes"),
            new("tears", "~stream", "down", "POSS"),
            new("POSS", "jaw", "~drop"),
            new("POSS", "mind", "~race"),
            new("it", "was", "all", "a", "dream"),
            new("dark", "and", "stormy", "night"),
        ];
    }
}
