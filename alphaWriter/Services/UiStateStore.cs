namespace alphaWriter.Services
{
    /// <summary>
    /// Small key/value store for remembered layout (last workspace, open scene, collapsed
    /// chapters, sidebar width). Never throws: forgetting a layout detail is harmless,
    /// crashing over one is not.
    /// </summary>
    public interface IUiStateStore
    {
        string Get(string key, string defaultValue = "");
        void Set(string key, string value);
    }

    /// <summary>Backed by platform preferences (kept separate for sandboxed runs).</summary>
    public sealed class PreferencesUiStateStore : IUiStateStore
    {
        public string Get(string key, string defaultValue = "")
        {
            try { return AppPreferences.Get("ui." + key, defaultValue); }
            catch { return defaultValue; }
        }

        public void Set(string key, string value)
        {
            try { AppPreferences.Set("ui." + key, value); }
            catch { /* no platform preferences (unit tests) */ }
        }
    }

    /// <summary>In-memory store for tests.</summary>
    public sealed class MemoryUiStateStore : IUiStateStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string Get(string key, string defaultValue = "") => _values.GetValueOrDefault(key, defaultValue);
        public void Set(string key, string value) => _values[key] = value;
    }
}
