using Catalyst;
using Catalyst.Models;
using Mosaik.Core;
using alphaWriter.Models;
using System.IO;

namespace alphaWriter.Services.Nlp
{
    public class NerService : INerService
    {
        private Pipeline? _pipeline;

        // Maps WikiNER entity type labels → our domain enum.
        // Catalyst.Models.English v1.0.30952 uses full English names ("Person",
        // "Organization", "Location") rather than the CoNLL short codes.
        // Both forms are included for forward/backward compatibility.
        private static readonly Dictionary<string, NerEntityType> TagMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // CoNLL-style short codes
                ["PER"]          = NerEntityType.Character,
                ["LOC"]          = NerEntityType.Location,
                ["ORG"]          = NerEntityType.Item,
                // Full English names (Catalyst.Models.English WikiNER)
                ["Person"]       = NerEntityType.Character,
                ["Location"]     = NerEntityType.Location,
                ["Organization"] = NerEntityType.Item,
                // Upper-case variants
                ["PERSON"]       = NerEntityType.Character,
                ["LOCATION"]     = NerEntityType.Location,
                ["ORGANIZATION"] = NerEntityType.Item,
                // Miscellaneous → treat as Item
                ["MISC"]         = NerEntityType.Item,
                ["Miscellaneous"]= NerEntityType.Item,
            };

        /// <summary>
        /// Raw entity type strings seen in the last ExtractEntities call, before TagMap
        /// filtering. Populated when at least one entity span was found but had an
        /// unmapped type — used to surface diagnostic info to the UI.
        /// </summary>
        public IReadOnlyList<string> LastRawEntityTypes { get; private set; } = [];

        public bool IsLoaded => _pipeline is not null;

        public Task LoadAsync(CancellationToken ct = default)
        {
            if (IsLoaded) return Task.CompletedTask;
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await CatalystInitializer.EnsureInitializedAsync(ct);
                ct.ThrowIfCancellationRequested();

                _pipeline = await Pipeline.ForAsync(Language.English);
                if (_pipeline is null)
                    throw new InvalidOperationException(
                        "Catalyst pipeline returned null — models may not have been registered.");

                // Catalyst.Models.English.Register() does NOT register WikiNER.
                // Manually extract wikiner.bin from the assembly's embedded resources
                // to the correct DiskStorage path so FromStoreAsync can find it.
                await EnsureWikiNerExtractedAsync(ct);

                // Add WikiNER entity recognizer. The bundled model version varies by
                // Catalyst.Models.English release, so try the most common values.
                AveragePerceptronEntityRecognizer? ner = null;
                foreach (var ver in new[] { 0, 1, 2, 3 })
                {
                    try
                    {
                        ner = await AveragePerceptronEntityRecognizer.FromStoreAsync(
                            Language.English, ver, "WikiNER");
                        if (ner is not null) break;
                    }
                    catch { /* try next version */ }
                }

                if (ner is null)
                    throw new InvalidOperationException(
                        "WikiNER model not found in Catalyst store (tried versions 0–3). " +
                        "Verify Catalyst.Models.English is installed and Register() succeeded.");

                _pipeline.Add(ner);
            }, ct);
        }

        /// <summary>
        /// Extracts <c>wikiner.bin</c> from <c>Catalyst.Models.English</c>'s embedded resources
        /// into the Catalyst DiskStorage path if it isn't already there.
        /// DiskStorage path format: Models/en/AveragePerceptronEntityRecognizer/v{ver:D6}/model-{tag}-v{ver:D6}.bin
        /// </summary>
        private static async Task EnsureWikiNerExtractedAsync(CancellationToken ct)
        {
            var nerDir = Path.Combine(
                CatalystInitializer.ModelStoragePath,
                "Models", "en", "AveragePerceptronEntityRecognizer", "v000000");
            var nerFile = Path.Combine(nerDir, "model-WikiNER-v000000.bin");

            if (File.Exists(nerFile)) return; // already extracted on a previous run

            Directory.CreateDirectory(nerDir);

            var assembly = typeof(Catalyst.Models.English).Assembly;
            using var stream = assembly.GetManifestResourceStream(
                "Catalyst.Models.English.Resources.wikiner.bin");

            if (stream is null)
                throw new InvalidOperationException(
                    "wikiner.bin not found in Catalyst.Models.English assembly resources. " +
                    "Verify the Catalyst.Models.English NuGet package is installed.");

            using var fs = new FileStream(nerFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs, ct);
        }

        public void UnloadModel() => _pipeline = null;

        public List<(string Name, NerEntityType Type, int Count)> ExtractEntities(
            IReadOnlyList<string> sentences)
        {
            if (!IsLoaded)
                throw new InvalidOperationException(
                    "NER model not loaded. Call LoadAsync first.");

            var counts   = new Dictionary<(string name, NerEntityType type), int>();
            var rawTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;

                var doc = new Document(sentence, Language.English);
                _pipeline!.ProcessSingle(doc);

                foreach (var span in doc)
                {
                    foreach (var entity in span.GetEntities())
                    {
                        var typeStr = entity.EntityType.Type ?? string.Empty;
                        if (string.IsNullOrEmpty(typeStr) || typeStr == "O") continue;

                        // Strip CoNLL IOB prefix: "B-Person" → "Person", "B-PER" → "PER"
                        string baseTag = typeStr.Length > 2 && typeStr[1] == '-'
                            ? typeStr[2..]
                            : typeStr;

                        rawTypes.Add(baseTag); // always record for diagnostics

                        if (!TagMap.TryGetValue(baseTag, out var entityType)) continue;

                        var name = entity.Value?.Trim() ?? string.Empty;
                        if (name.Length <= 1) continue; // skip single-char noise

                        var key = (name, entityType);
                        counts[key] = counts.TryGetValue(key, out var prev) ? prev + 1 : 1;
                    }
                }
            }

            // Expose raw type labels so the caller can diagnose unexpected label names.
            if (rawTypes.Count > 0)
                LastRawEntityTypes = rawTypes.ToList();

            return counts
                .Select(kv => (kv.Key.name, kv.Key.type, kv.Value))
                .OrderByDescending(x => x.Item3)
                .ToList();
        }

        public void Dispose() => _pipeline = null;
    }
}
