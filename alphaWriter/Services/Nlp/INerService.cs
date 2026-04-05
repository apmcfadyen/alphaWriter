using alphaWriter.Models;

namespace alphaWriter.Services.Nlp
{
    public interface INerService : IDisposable
    {
        bool IsLoaded { get; }
        Task LoadAsync(CancellationToken ct = default);
        void UnloadModel();

        /// <summary>
        /// Runs WikiNER on the provided sentences and returns a deduplicated list
        /// of (Name, EntityType, Count) sorted by descending frequency.
        /// </summary>
        List<(string Name, NerEntityType Type, int Count)> ExtractEntities(
            IReadOnlyList<string> sentences);

        /// <summary>
        /// The raw entity type label strings seen in the last ExtractEntities call
        /// (e.g. "Person", "Organization", "Location"). Useful for diagnosing
        /// unexpected label names when results are empty.
        /// </summary>
        IReadOnlyList<string> LastRawEntityTypes { get; }
    }
}
