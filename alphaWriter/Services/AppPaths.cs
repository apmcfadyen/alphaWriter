namespace alphaWriter.Services
{
    /// <summary>
    /// Where the app keeps the author's data. Normally the platform app-data folder; when
    /// ALPHAWRITER_DATA_DIR is set (UI snapshot runs, experiments) books, images and the
    /// analysis cache go there instead, so a second instance can never touch the real library.
    /// Downloaded models stay shared — they are read-only and large.
    /// </summary>
    public static class AppPaths
    {
        public const string DataDirVariable = "ALPHAWRITER_DATA_DIR";

        private static string? Override =>
            Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } dir ? dir : null;

        /// <summary>True when running against a sandbox data folder.</summary>
        public static bool IsSandboxed => Override is not null;

        public static string DataDirectory => Override ?? FileSystem.AppDataDirectory;
    }

    /// <summary>
    /// Preferences that stay separate for sandboxed runs, so a snapshot run cannot change
    /// which book the author's own copy opens, or whether its panels are shown.
    /// </summary>
    public static class AppPreferences
    {
        private static string Key(string key) => AppPaths.IsSandboxed ? $"sandbox.{key}" : key;

        public static T Get<T>(string key, T defaultValue) => Preferences.Default.Get(Key(key), defaultValue);

        public static void Set<T>(string key, T value) => Preferences.Default.Set(Key(key), value);
    }
}
