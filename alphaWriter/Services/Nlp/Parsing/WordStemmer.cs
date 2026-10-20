using System;
using System.Collections.Generic;

namespace alphaWriter.Services.Nlp.Parsing
{
    /// <summary>
    /// Maps inflected forms to a shared word-family key so rules can treat
    /// walk / walked / walking as one word. Irregular verbs are looked up first,
    /// everything else goes through the classic Porter (1980) stemmer.
    /// Keys are for comparison only — they are not always dictionary words.
    /// </summary>
    public static class WordStemmer
    {
        private static readonly Dictionary<string, string> Irregular = BuildIrregulars();

        public static string Stem(string word)
        {
            if (string.IsNullOrEmpty(word)) return string.Empty;
            var w = word.ToLowerInvariant().Replace('’', '\'');
            if (Irregular.TryGetValue(w, out var baseForm)) w = baseForm;
            // Possessive / contraction tails do not change the word family
            if (w.EndsWith("'s")) w = w[..^2];
            if (w.Length <= 2) return w;
            foreach (var c in w)
                if (!char.IsLetter(c)) return w;
            return Porter(w);
        }

        // ── Irregular verb forms → base ─────────────────────────────────────

        private static Dictionary<string, string> BuildIrregulars()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            void Add(string baseForm, params string[] forms)
            {
                foreach (var f in forms) d[f] = baseForm;
            }

            Add("be", "am", "is", "are", "was", "were", "been", "being");
            Add("have", "has", "had", "having");
            Add("do", "does", "did", "done", "doing");
            Add("go", "goes", "went", "gone");
            Add("say", "says", "said");
            Add("make", "made");
            Add("take", "took", "taken");
            Add("see", "saw", "seen");
            Add("come", "came");
            Add("know", "knew", "known");
            Add("get", "got", "gotten");
            Add("give", "gave", "given");
            Add("find", "found");
            Add("think", "thought");
            Add("tell", "told");
            Add("become", "became");
            Add("feel", "felt");
            Add("leave", "left");
            Add("bring", "brought");
            Add("begin", "began", "begun");
            Add("keep", "kept");
            Add("hold", "held");
            Add("write", "wrote", "written");
            Add("stand", "stood");
            Add("hear", "heard");
            Add("mean", "meant");
            Add("meet", "met");
            Add("run", "ran");
            Add("pay", "paid");
            Add("sit", "sat");
            Add("speak", "spoke", "spoken");
            Add("lie", "lay", "lain");
            Add("lead", "led");
            Add("grow", "grew", "grown");
            Add("lose", "lost");
            Add("fall", "fell", "fallen");
            Add("send", "sent");
            Add("build", "built");
            Add("understand", "understood");
            Add("draw", "drew", "drawn");
            Add("break", "broke", "broken");
            Add("spend", "spent");
            Add("rise", "rose", "risen");
            Add("drive", "drove", "driven");
            Add("buy", "bought");
            Add("wear", "wore", "worn");
            Add("choose", "chose", "chosen");
            Add("seek", "sought");
            Add("throw", "threw", "thrown");
            Add("catch", "caught");
            Add("deal", "dealt");
            Add("win", "won");
            Add("forget", "forgot", "forgotten");
            Add("sell", "sold");
            Add("fight", "fought");
            Add("teach", "taught");
            Add("eat", "ate", "eaten");
            Add("sing", "sang", "sung");
            Add("drink", "drank", "drunk");
            Add("swim", "swam", "swum");
            Add("fly", "flew", "flown");
            Add("hide", "hid", "hidden");
            Add("ride", "rode", "ridden");
            Add("shake", "shook", "shaken");
            Add("steal", "stole", "stolen");
            Add("strike", "struck", "stricken");
            Add("swear", "swore", "sworn");
            Add("tear", "tore", "torn");
            Add("wake", "woke", "woken");
            Add("bite", "bit", "bitten");
            Add("freeze", "froze", "frozen");
            Add("sleep", "slept");
            Add("sweep", "swept");
            Add("weep", "wept");
            Add("creep", "crept");
            Add("kneel", "knelt");
            Add("dream", "dreamt");
            Add("feed", "fed");
            Add("flee", "fled");
            Add("hang", "hung");
            Add("light", "lit");
            Add("shine", "shone");
            Add("shoot", "shot");
            Add("slide", "slid");
            Add("spin", "spun");
            Add("stick", "stuck");
            Add("sting", "stung");
            Add("swing", "swung");
            Add("cling", "clung");
            Add("fling", "flung");
            Add("dig", "dug");
            Add("bleed", "bled");
            Add("bend", "bent");
            Add("lend", "lent");
            Add("bind", "bound");
            Add("wind", "wound");
            Add("grind", "ground");
            Add("tread", "trod", "trodden");
            Add("forgive", "forgave", "forgiven");
            Add("arise", "arose", "arisen");
            Add("awake", "awoke", "awoken");
            Add("shrink", "shrank", "shrunk");
            Add("sink", "sank", "sunk");
            Add("spring", "sprang", "sprung");
            Add("stink", "stank", "stunk");
            Add("ring", "rang", "rung");
            Add("man", "men");
            Add("woman", "women");
            Add("child", "children");
            Add("foot", "feet");
            Add("tooth", "teeth");
            Add("mouse", "mice");
            return d;
        }

        // ── Porter stemmer ──────────────────────────────────────────────────

        private static bool IsConsonant(string w, int i)
        {
            switch (w[i])
            {
                case 'a': case 'e': case 'i': case 'o': case 'u': return false;
                case 'y': return i == 0 || !IsConsonant(w, i - 1);
                default: return true;
            }
        }

        /// <summary>Porter's m(): number of VC sequences in w[0..end].</summary>
        private static int Measure(string w, int end)
        {
            int n = 0, i = 0;
            while (true)
            {
                if (i > end) return n;
                if (!IsConsonant(w, i)) break;
                i++;
            }
            i++;
            while (true)
            {
                while (true)
                {
                    if (i > end) return n;
                    if (IsConsonant(w, i)) break;
                    i++;
                }
                i++;
                n++;
                while (true)
                {
                    if (i > end) return n;
                    if (!IsConsonant(w, i)) break;
                    i++;
                }
                i++;
            }
        }

        private static bool HasVowel(string w, int end)
        {
            for (int i = 0; i <= end; i++)
                if (!IsConsonant(w, i)) return true;
            return false;
        }

        private static bool EndsDoubleConsonant(string w) =>
            w.Length >= 2 && w[^1] == w[^2] && IsConsonant(w, w.Length - 1);

        /// <summary>cvc where the final c is not w, x or y.</summary>
        private static bool EndsCvc(string w)
        {
            int i = w.Length - 1;
            if (i < 2 || !IsConsonant(w, i) || IsConsonant(w, i - 1) || !IsConsonant(w, i - 2))
                return false;
            return w[i] is not ('w' or 'x' or 'y');
        }

        private static bool TryReplace(ref string w, string suffix, string replacement, int minMeasure)
        {
            if (!w.EndsWith(suffix, StringComparison.Ordinal)) return false;
            var stem = w[..^suffix.Length];
            if (Measure(stem, stem.Length - 1) > minMeasure)
                w = stem + replacement;
            return true;
        }

        private static string Porter(string w)
        {
            // Step 1a
            if (w.EndsWith("sses")) w = w[..^2];
            else if (w.EndsWith("ies")) w = w[..^2];
            else if (w.EndsWith("ss")) { }
            else if (w.EndsWith("s")) w = w[..^1];

            // Step 1b
            bool extra = false;
            if (w.EndsWith("eed"))
            {
                if (Measure(w, w.Length - 4) > 0) w = w[..^1];
            }
            else if (w.EndsWith("ed") && HasVowel(w, w.Length - 3))
            {
                w = w[..^2]; extra = true;
            }
            else if (w.EndsWith("ing") && HasVowel(w, w.Length - 4))
            {
                w = w[..^3]; extra = true;
            }
            if (extra)
            {
                if (w.EndsWith("at") || w.EndsWith("bl") || w.EndsWith("iz")) w += "e";
                else if (EndsDoubleConsonant(w) && w[^1] is not ('l' or 's' or 'z')) w = w[..^1];
                else if (Measure(w, w.Length - 1) == 1 && EndsCvc(w)) w += "e";
            }

            // Step 1c
            if (w.EndsWith("y") && w.Length > 1 && HasVowel(w, w.Length - 2))
                w = w[..^1] + "i";

            // Step 2
            string[,] step2 =
            {
                { "ational", "ate" }, { "tional", "tion" }, { "enci", "ence" }, { "anci", "ance" },
                { "izer", "ize" }, { "bli", "ble" }, { "alli", "al" }, { "entli", "ent" },
                { "eli", "e" }, { "ousli", "ous" }, { "ization", "ize" }, { "ation", "ate" },
                { "ator", "ate" }, { "alism", "al" }, { "iveness", "ive" }, { "fulness", "ful" },
                { "ousness", "ous" }, { "aliti", "al" }, { "iviti", "ive" }, { "biliti", "ble" },
                { "logi", "log" }
            };
            for (int i = 0; i < step2.GetLength(0); i++)
                if (TryReplace(ref w, step2[i, 0], step2[i, 1], 0)) break;

            // Step 3
            string[,] step3 =
            {
                { "icate", "ic" }, { "ative", "" }, { "alize", "al" }, { "iciti", "ic" },
                { "ical", "ic" }, { "ful", "" }, { "ness", "" }
            };
            for (int i = 0; i < step3.GetLength(0); i++)
                if (TryReplace(ref w, step3[i, 0], step3[i, 1], 0)) break;

            // Step 4
            string[] step4 =
            [
                "al", "ance", "ence", "er", "ic", "able", "ible", "ant", "ement", "ment",
                "ent", "ion", "ou", "ism", "ate", "iti", "ous", "ive", "ize"
            ];
            foreach (var suffix in step4)
            {
                if (!w.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var stem = w[..^suffix.Length];
                if (Measure(stem, stem.Length - 1) > 1)
                {
                    if (suffix == "ion")
                    {
                        if (stem.Length > 0 && stem[^1] is 's' or 't') w = stem;
                    }
                    else w = stem;
                }
                break;
            }

            // Step 5a
            if (w.EndsWith("e"))
            {
                var stem = w[..^1];
                int m = Measure(stem, stem.Length - 1);
                if (m > 1 || (m == 1 && !EndsCvc(stem))) w = stem;
            }

            // Step 5b
            if (w.EndsWith("ll") && Measure(w, w.Length - 1) > 1) w = w[..^1];

            return w;
        }
    }
}
