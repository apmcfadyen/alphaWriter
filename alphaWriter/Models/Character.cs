namespace alphaWriter.Models
{
    public class Character
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Aka { get; set; } = string.Empty;

        /// <summary>
        /// "she/her", "he/him", "they/them", or empty when not set. Used by the editorial
        /// rules to work out who a pronoun can refer to; when empty, it is guessed from the text.
        /// </summary>
        public string Pronouns { get; set; } = string.Empty;
    }

    /// <summary>Which third-person pronouns refer to a character.</summary>
    public enum PronounSet { Unknown, She, He, They }

    public static class PronounSets
    {
        public static readonly IReadOnlyList<string> Options = ["", "she/her", "he/him", "they/them"];

        public static PronounSet Parse(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            var v when v.StartsWith("she") => PronounSet.She,
            var v when v.StartsWith("he") => PronounSet.He,
            var v when v.StartsWith("they") => PronounSet.They,
            _ => PronounSet.Unknown
        };

        public static string Label(PronounSet set) => set switch
        {
            PronounSet.She => "she/her",
            PronounSet.He => "he/him",
            PronounSet.They => "they/them",
            _ => string.Empty
        };

        /// <summary>The set a single pronoun word belongs to ("his" → He), or Unknown.</summary>
        public static PronounSet Of(string word) => word.ToLowerInvariant() switch
        {
            "she" or "her" or "hers" or "herself" => PronounSet.She,
            "he" or "him" or "his" or "himself" => PronounSet.He,
            _ => PronounSet.Unknown
        };
    }
}
