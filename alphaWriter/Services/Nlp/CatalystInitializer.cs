using Catalyst;
using Mosaik.Core;
using System.IO;

namespace alphaWriter.Services.Nlp
{
    /// <summary>
    /// Ensures Catalyst's model storage is initialized exactly once,
    /// shared across PosTaggingService and NerService.
    /// </summary>
    internal static class CatalystInitializer
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static bool _initialized;

        internal static string ModelStoragePath =>
            Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory,
                         "models", "catalyst");

        public static async Task EnsureInitializedAsync(CancellationToken ct = default)
        {
            if (_initialized) return;
            await _lock.WaitAsync(ct);
            try
            {
                if (_initialized) return;
                Directory.CreateDirectory(ModelStoragePath);
                Storage.Current = new DiskStorage(ModelStoragePath);

                // Register() fires async internal tasks and returns void immediately.
                // Poll Pipeline.ForAsync until it returns a non-null pipeline, which
                // confirms the async registrations have actually completed.
                Catalyst.Models.English.Register();

                Pipeline? probe = null;
                for (int attempt = 0; attempt < 60 && probe is null; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(250, ct);
                    probe = await Pipeline.ForAsync(Language.English);
                }

                if (probe is null)
                    throw new InvalidOperationException(
                        "Catalyst models failed to initialize after 15 seconds. " +
                        "Ensure Catalyst.Models.English is correctly referenced.");

                _initialized = true;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
