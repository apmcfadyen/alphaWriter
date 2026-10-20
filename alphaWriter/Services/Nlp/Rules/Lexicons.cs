using System;
using System.Collections.Generic;
using System.Linq;
using alphaWriter.Services.Nlp.Parsing;

namespace alphaWriter.Services.Nlp.Rules
{
    /// <summary>Word lists used by the built-in rules.</summary>
    internal static class Lexicons
    {
        private static HashSet<string> Set(params string[] words) =>
            new(words, StringComparer.OrdinalIgnoreCase);

        public static readonly HashSet<string> BeVerbs =
            Set("am", "is", "are", "was", "were", "be", "been", "being", "'s", "'re", "'m");

        public static readonly HashSet<string> Negations =
            Set("not", "never", "no", "hardly", "barely", "scarcely", "nor", "neither", "n't", "without");

        public static readonly HashSet<string> SubjectPronouns =
            Set("i", "he", "she", "they", "we", "you");

        /// <summary>Filler adverbs that can sit between a subject and its verb.</summary>
        public static readonly HashSet<string> InterveningAdverbs =
            Set("suddenly", "just", "still", "also", "already", "almost", "only", "really",
                "finally", "then", "now", "somehow", "simply", "truly");

        /// <summary>Words ending in -ly that are not manner adverbs.</summary>
        public static readonly HashSet<string> NotAdverbs = Set(
            "family", "only", "early", "reply", "holy", "ugly", "belly", "bully", "jelly", "lily",
            "rally", "ally", "supply", "apply", "imply", "comply", "multiply", "rely", "fly",
            "daily", "weekly", "monthly", "yearly", "hourly", "nightly", "curly", "silly",
            "lonely", "lovely", "friendly", "unfriendly", "likely", "unlikely", "elderly",
            "costly", "deadly", "lively", "manly", "orderly", "sickly", "surly", "wily",
            "woolly", "chilly", "hilly", "jolly", "folly", "holly", "gully", "sully", "tally",
            "dally", "assembly", "anomaly", "monopoly", "butterfly", "dragonfly", "firefly",
            "bodily", "cowardly", "comely", "courtly", "ghastly", "ghostly", "godly", "heavenly",
            "homely", "kingly", "leisurely", "lowly", "masterly", "miserly", "motherly",
            "fatherly", "brotherly", "sisterly", "neighborly", "portly", "princely", "queenly",
            "saintly", "scholarly", "seemly", "shapely", "slovenly", "smelly", "stately",
            "timely", "unruly", "unseemly", "wobbly", "worldly", "bubbly", "crumbly", "gangly",
            "grisly", "grizzly", "prickly", "sparkly", "squiggly", "wrinkly", "burly", "measly",
            "oily", "wooly", "doily", "melancholy", "italy", "july", "anomaly", "homily", "rely");

        /// <summary>Verbs that introduce a told (rather than shown) emotional state.</summary>
        public static readonly HashSet<string> FeelingVerbStems =
            new[] { "feel", "seem", "look", "appear", "be", "become", "grow" }
                .Select(WordStemmer.Stem)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>Emotion words that name a feeling directly.</summary>
        public static readonly HashSet<string> TellingEmotionWords = Set(
            "angry", "angered", "sad", "sadness", "happy", "happiness", "afraid",
            "scared", "worried", "nervous", "excited", "surprised", "shocked",
            "confused", "upset", "frustrated", "embarrassed",
            "ashamed", "guilty", "proud", "jealous", "lonely", "bored", "curious",
            "disgusted", "horrified", "terrified", "anxious", "depressed", "miserable",
            "hopeful", "hopeless", "joyful", "cheerful", "melancholy", "content",
            "distressed", "frightened", "startled", "elated", "bitter", "resentful",
            "grief", "sorrow", "joy", "fear", "rage", "fury", "panic", "dread",
            "despair", "ecstasy", "bliss", "guilt", "shame", "envy", "jealousy",
            "resentment", "bitterness", "disappointment", "regret", "remorse",
            "apprehensive", "heartbroken", "devastated", "overjoyed", "thrilled",
            "gloomy", "forlorn", "wistful", "relieved", "humiliated", "shamed",
            "delighted", "enraged", "bewildered");

        public static readonly HashSet<string> SubordinatingConjunctions = Set(
            "because", "although", "though", "unless", "until",
            "whereas", "whenever", "wherever", "whoever", "whatever",
            "however", "if", "when", "while", "since", "before",
            "after", "as", "whether", "once", "provided");

        /// <summary>Function words excluded from echo detection.</summary>
        public static readonly HashSet<string> Stopwords = Set(
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "must", "shall", "can", "to", "of", "in",
            "on", "at", "by", "for", "with", "as", "from", "up", "out", "into",
            "then", "than", "this", "that", "these", "those", "it", "its", "his",
            "her", "our", "your", "their", "my", "we", "they", "he", "she",
            "and", "or", "but", "not", "no", "so", "if", "when", "where", "how",
            "all", "any", "both", "each", "more", "most", "other", "some", "just",
            "also", "very", "about", "over", "such", "only", "here", "there",
            "what", "who", "him", "them", "me", "us", "you", "still",
            "even", "back", "after", "again", "down", "through", "never", "now",
            "well", "said", "which", "while", "would", "could", "should", "every",
            "before", "because", "around", "against", "between", "without", "under");

        /// <summary>Irregular past participles (regular ones end in -ed).</summary>
        public static readonly HashSet<string> IrregularParticiples = Set(
            "known", "seen", "taken", "given", "done", "gone", "made", "found", "held", "told",
            "thought", "brought", "caught", "taught", "built", "sent", "left", "kept", "lost",
            "hit", "put", "set", "cut", "shut", "read", "won", "hung", "shot", "bound", "born",
            "borne", "broken", "chosen", "driven", "eaten", "fallen", "forgotten", "frozen",
            "hidden", "ridden", "risen", "shaken", "spoken", "stolen", "sworn", "torn", "thrown",
            "woken", "worn", "written", "beaten", "bitten", "bent", "bled", "fed", "fought",
            "fled", "flung", "ground", "heard", "laid", "led", "lent", "lit", "meant", "met",
            "paid", "said", "sold", "sought", "spent", "spun", "struck", "stuck", "stung",
            "swept", "swung", "understood", "wept", "wound", "dealt", "dug", "drawn",
            "forgiven", "forbidden", "grown", "overcome", "slain", "sunk", "sung", "drunk",
            "begun", "bought", "felt", "hurt", "cast", "spread", "quit", "split", "burst",
            "shed", "let", "bet", "shown", "sewn", "sown", "swollen", "undone", "withdrawn",
            "overtaken", "overthrown", "mistaken", "shorn", "slung", "spat", "strewn", "stridden",
            "striven", "thrust", "trodden", "upset", "woven", "wrung", "clung", "crept", "knelt");

        /// <summary>
        /// Participles that usually describe a state rather than an action ("she was tired",
        /// "he was supposed to…"). Not flagged as passive unless an agent ("by …") follows.
        /// </summary>
        public static readonly HashSet<string> StatalParticiples = Set(
            "tired", "bored", "interested", "excited", "worried", "scared", "frightened",
            "terrified", "surprised", "shocked", "pleased", "satisfied", "disappointed",
            "confused", "embarrassed", "ashamed", "amazed", "annoyed", "exhausted", "married",
            "dressed", "finished", "gone", "done", "lost", "used", "supposed", "determined",
            "convinced", "relieved", "prepared", "located", "situated", "involved", "related",
            "based", "closed", "locked", "crowded", "engaged", "divorced", "retired", "drunk",
            "stuck", "stunned", "startled", "puzzled", "delighted", "thrilled", "horrified",
            "fascinated", "obsessed", "disgusted", "overwhelmed", "concerned", "qualified",
            "aged", "armed", "tanned", "freckled", "bearded");
    }
}
