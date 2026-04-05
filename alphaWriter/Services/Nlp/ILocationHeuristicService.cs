namespace alphaWriter.Services.Nlp
{
    public interface ILocationHeuristicService
    {
        /// <summary>
        /// Scans POS-tagged sentences for spatial-preposition + proper-noun patterns
        /// that suggest location names. Filters out entities that co-occur with
        /// personal pronouns (likely characters) and any names in <paramref name="knownCharacterNames"/>.
        /// Returns deduplicated (Name, Count) sorted by descending frequency.
        /// </summary>
        List<(string Name, int Count)> FindLocationCandidates(
            IReadOnlyList<string> sentences,
            IReadOnlyList<(string Value, string Pos)>[] taggedSentences,
            IReadOnlySet<string> knownCharacterNames);
    }
}
