#if DEBUG
using alphaWriter.Models;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Debug builds only: puts the UI into a named state at start-up so screenshot runs
    /// (tools/ui-snapshots) can capture each workspace without driving the mouse.
    /// </summary>
    /// <remarks>
    /// Read from ALPHAWRITER_UI_STATE as ';'-separated key=value pairs, applied in order:
    /// <c>book=first</c>, <c>scene=&lt;title&gt;</c>, <c>character=&lt;name&gt;</c>,
    /// <c>insight=&lt;Frequency|Problems|Characters|DailyProgress|report:Entities&gt;</c>,
    /// <c>scope=scene|book</c>, <c>inspector=on|off</c>, <c>sidebar=on|off</c>,
    /// <c>settings=on</c>, <c>analyze=on</c> (runs a whole-book analysis), <c>section=&lt;report element id&gt;</c>, <c>filter=&lt;editor&gt;</c>,
    /// <c>workspace=&lt;Write|StoryBible|Revise|Insights|Book&gt;</c>.
    /// </remarks>
    public partial class WriterViewModel
    {
        /// <summary>Element id the analysis report scrolls to once loaded (<c>section=arc</c>).</summary>
        public string? ReportScrollTarget { get; private set; }

        public async Task ApplyUiStateFromEnvironment()
        {
            var spec = Environment.GetEnvironmentVariable(UiStateVariable);
            if (string.IsNullOrWhiteSpace(spec)) return;

            foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length != 2) continue;
                var (key, value) = (kv[0].ToLowerInvariant(), kv[1]);

                switch (key)
                {
                    case "book" when SelectedBook is null && Books.Count > 0:
                        SelectedBook = value == "first"
                            ? Books[0]
                            : Books.FirstOrDefault(b => b.Title == value) ?? Books[0];
                        break;
                    case "scene":
                        foreach (var chapter in SelectedBook?.Chapters ?? [])
                        {
                            var scene = chapter.Scenes.FirstOrDefault(s => s.Title == value);
                            if (scene is null) continue;
                            SelectedChapter = chapter;
                            SelectedScene = scene;
                            break;
                        }
                        break;
                    case "character":
                        SelectedCharacter = SelectedBook?.Characters.FirstOrDefault(c => c.Name == value);
                        break;
                    case "insight":
                        OpenInsight(value);
                        break;
                    case "analyze" when value == "on":
                        await RunAnalysis();
                        break;
                    case "section":
                        ReportScrollTarget = value;
                        break;
                    case "filter":
                        SelectedCategoryFilter = value;
                        break;
                    case "scope":
                        ReviseScopeIsScene = value == "scene";
                        break;
                    case "inspector":
                        IsMetadataPanelVisible = value == "on";
                        break;
                    case "sidebar":
                        IsSidebarVisible = value == "on";
                        break;
                    case "settings" when value == "on":
                        if (!IsRuleSettingsVisible) ToggleRuleSettings();
                        break;
                    case "workspace":
                        SetWorkspace(value);
                        break;
                }
            }
        }
    }
}
#endif
