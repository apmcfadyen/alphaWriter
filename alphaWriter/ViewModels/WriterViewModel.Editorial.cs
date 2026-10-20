using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp;
using alphaWriter.Services.Nlp.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace alphaWriter.ViewModels
{
    /// <summary>
    /// Editorial suite state: which notes are visible (the author's style sheet),
    /// live per-scene analysis while editing, in-editor annotations, and note actions.
    /// </summary>
    public partial class WriterViewModel
    {
        private static readonly JsonSerializerOptions CamelCase =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>Raised when the current scene's visible annotations change.</summary>
        public event Action? AnnotationsChanged;

        /// <summary>Raised before a note's scene is selected, so the editor can focus its words once loaded.</summary>
        public event Action<NlpNote>? NoteNavigationRequested;

        [ObservableProperty]
        private ObservableCollection<RuleToggleViewModel> ruleSettings = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotesListVisible))]
        [NotifyPropertyChangedFor(nameof(IsFilterRowVisible))]
        [NotifyPropertyChangedFor(nameof(RuleSettingsToggleText))]
        private bool isRuleSettingsVisible;

        public bool IsNotesListVisible => !IsRuleSettingsVisible && !IsPassPickerVisible;

        public string RuleSettingsToggleText => IsRuleSettingsVisible ? "← Back to notes" : "⚙ Settings";

        public bool IsFilterRowVisible => HasAnalysisResults && !IsRuleSettingsVisible;

        [ObservableProperty]
        private string selectedGenre = EditorialStyleSheet.DefaultGenre;

        [ObservableProperty]
        private string allowedWordsText = string.Empty;

        public IReadOnlyList<string> GenreOptions => GenrePresets.Genres;

        // ── Revise note list ──────────────────────────────────────────────────

        /// <summary>Visible notes grouped by scene, in reading order.</summary>
        [ObservableProperty]
        private ObservableCollection<NoteGroup> reviseNoteGroups = [];

        /// <summary>True: only the open scene's notes. False: the whole book.</summary>
        [ObservableProperty]
        private bool reviseScopeIsScene = true;

        public string ReviseSummary
        {
            get
            {
                int count = ReviseNoteGroups.Sum(g => g.Count);
                if (count == 0) return ReviseScopeIsScene && SelectedScene is not null
                    ? "No notes for this scene."
                    : "No notes.";
                var where = ReviseScopeIsScene && SelectedScene is not null ? "in this scene" : "in the book";
                return $"{count} note{(count == 1 ? "" : "s")} {where}";
            }
        }

        partial void OnReviseScopeIsSceneChanged(bool value) => RebuildReviseGroups();

        [RelayCommand]
        private void SetReviseScope(string scope) => ReviseScopeIsScene = scope == "scene";

        /// <summary>
        /// Groups the filtered notes (<see cref="NlpNotes"/>) by scene in book order, with
        /// book- and chapter-level notes first. Within a scene, notes follow the text.
        /// </summary>
        internal void RebuildReviseGroups()
        {
            var notes = NlpNotes.AsEnumerable();
            if (IsPassActive)
                notes = VisibleNotes.Where(n => n.RuleId == ActivePassRuleId);
            bool sceneOnly = ReviseScopeIsScene && SelectedScene is not null;
            if (sceneOnly)
                notes = notes.Where(n => n.SceneId == SelectedScene!.Id);

            var bySceneId = notes.GroupBy(n => n.SceneId).ToDictionary(g => g.Key, g => g.ToList());
            var groups = new List<NoteGroup>();

            if (bySceneId.TryGetValue(string.Empty, out var bookLevel))
                groups.Add(new NoteGroup("Whole book & chapters", string.Empty, Order(bookLevel)));

            if (SelectedBook is not null)
            {
                foreach (var chapter in SelectedBook.Chapters)
                    foreach (var scene in chapter.Scenes)
                        if (bySceneId.TryGetValue(scene.Id, out var sceneNotes))
                            groups.Add(new NoteGroup($"{chapter.Title} › {scene.Title}", scene.Id, Order(sceneNotes)));
            }

            ReviseNoteGroups = new ObservableCollection<NoteGroup>(groups);
            OnPropertyChanged(nameof(ReviseSummary));
            UpdatePassProgress();
            RefreshSceneCard();

            static IEnumerable<NlpNote> Order(IEnumerable<NlpNote> ns) => ns
                .OrderBy(n => n.SentenceIndex ?? -1)
                .ThenBy(n => n.Severity switch { NlpNoteSeverity.Issue => 0, NlpNoteSeverity.Warning => 1, _ => 2 });
        }

        public int DismissedCount => SelectedBook?.StyleSheet?.DismissedFingerprints.Count ?? 0;

        public string DismissedSummary => DismissedCount == 0
            ? "No dismissed notes."
            : $"{DismissedCount} dismissed note{(DismissedCount == 1 ? "" : "s")} hidden.";

        private bool _suppressPreferenceEvents;

        private EditorialStyleSheet? StyleSheet
        {
            get
            {
                if (SelectedBook is null) return null;
                return SelectedBook.StyleSheet ??= new EditorialStyleSheet();
            }
        }

        // ── Visibility ────────────────────────────────────────────────────────

        /// <summary>
        /// A note is shown unless its rule is off (book-wide or for its scene), the author
        /// dismissed it, or its evidence is a word on the style sheet's allow-list.
        /// </summary>
        internal bool IsNoteVisible(NlpNote note)
        {
            var sheet = StyleSheet;
            if (sheet is null) return true;
            if (!sheet.IsRuleEnabled(note.RuleId, string.IsNullOrEmpty(note.SceneId) ? null : note.SceneId))
                return false;
            if (sheet.IsDismissed(note.Fingerprint)) return false;
            if (!string.IsNullOrEmpty(note.Evidence) && !note.Evidence.Contains(' ') && sheet.IsWordAllowed(note.Evidence))
                return false;
            return true;
        }

        private IEnumerable<NlpNote> VisibleNotes => _allNotes.Where(IsNoteVisible);

        // ── Settings panel ────────────────────────────────────────────────────

        /// <summary>Rebuilds the settings panel from the selected book's style sheet.</summary>
        internal void RefreshEditorialSettings()
        {
            _suppressPreferenceEvents = true;
            try
            {
                var sheet = StyleSheet;
                RuleSettings = new ObservableCollection<RuleToggleViewModel>(
                    RuleCatalog.All.Select(d => new RuleToggleViewModel(
                        d, sheet?.IsRuleEnabled(d.Id) ?? true, OnRuleToggled)));
                SelectedGenre = sheet?.Genre ?? EditorialStyleSheet.DefaultGenre;
                AllowedWordsText = sheet is null ? string.Empty : string.Join(", ", sheet.AllowedWords);
            }
            finally
            {
                _suppressPreferenceEvents = false;
            }
            OnPropertyChanged(nameof(DismissedCount));
            OnPropertyChanged(nameof(DismissedSummary));
        }

        private void OnRuleToggled(RuleToggleViewModel toggle)
        {
            var sheet = StyleSheet;
            if (sheet is null || _suppressPreferenceEvents) return;

            sheet.DisabledRuleIds.Remove(toggle.Id);
            if (!toggle.IsEnabled) sheet.DisabledRuleIds.Add(toggle.Id);
            OnEditorialPreferencesChanged();
        }

        partial void OnSelectedGenreChanged(string value)
        {
            var sheet = StyleSheet;
            if (sheet is null || _suppressPreferenceEvents || sheet.Genre == value) return;
            sheet.Genre = value;
            OnEditorialPreferencesChanged();
            // Thresholds are applied when rules run — refresh the open scene immediately.
            if (SelectedScene is not null) ScheduleLiveAnalysis(SelectedScene, 0);
        }

        /// <summary>Commits the allow-list text box (comma or newline separated).</summary>
        [RelayCommand]
        private void SaveAllowedWords()
        {
            var sheet = StyleSheet;
            if (sheet is null) return;
            sheet.AllowedWords = (AllowedWordsText ?? string.Empty)
                .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            OnEditorialPreferencesChanged();
        }

        [RelayCommand]
        private void ToggleRuleSettings()
        {
            if (!IsRuleSettingsVisible) RefreshEditorialSettings();
            IsRuleSettingsVisible = !IsRuleSettingsVisible;
            if (IsRuleSettingsVisible) IsPassPickerVisible = false;
        }

        [RelayCommand]
        private void RestoreDismissed()
        {
            var sheet = StyleSheet;
            if (sheet is null || sheet.DismissedFingerprints.Count == 0) return;
            sheet.DismissedFingerprints.Clear();
            sheet.SceneDisabledRuleIds.Clear();
            OnEditorialPreferencesChanged();
        }

        // ── Note actions ──────────────────────────────────────────────────────

        [RelayCommand]
        private void DismissNote(NlpNote? note)
        {
            if (note is null) return;
            ApplyNoteAction("dismiss", note.Fingerprint, note.RuleId, note.SceneId, note.Evidence);
        }

        /// <summary>
        /// Applies an action chosen on a note — from a note card or the editor's popover.
        /// Actions: dismiss, disable-scene, disable-rule, allow-word.
        /// </summary>
        public void ApplyNoteAction(string action, string? fingerprint, string? ruleId,
            string? sceneId, string? word, string? value = null)
        {
            if (action == "fix" && !string.IsNullOrEmpty(value))
            {
                ApplyFixAction(value, sceneId);
                return;
            }

            var sheet = StyleSheet;
            if (sheet is null) return;

            switch (action)
            {
                case "dismiss" when !string.IsNullOrEmpty(fingerprint):
                    if (!sheet.DismissedFingerprints.Contains(fingerprint))
                        sheet.DismissedFingerprints.Add(fingerprint);
                    break;
                case "disable-scene" when !string.IsNullOrEmpty(ruleId) && !string.IsNullOrEmpty(sceneId):
                    sheet.DisableRuleForScene(ruleId, sceneId);
                    break;
                case "disable-rule" when !string.IsNullOrEmpty(ruleId):
                    if (!sheet.DisabledRuleIds.Contains(ruleId)) sheet.DisabledRuleIds.Add(ruleId);
                    break;
                case "allow-word" when !string.IsNullOrWhiteSpace(word):
                    if (!sheet.IsWordAllowed(word)) sheet.AllowedWords.Add(word.Trim());
                    break;
                default:
                    return;
            }

            OnEditorialPreferencesChanged();
            if (IsRuleSettingsVisible) RefreshEditorialSettings();
        }

        /// <summary>Runs a note's <see cref="NlpNote.FixAction"/> from a note card.</summary>
        [RelayCommand]
        private void ApplyNoteFix(NlpNote? note)
        {
            if (note is null || !note.HasFixAction) return;
            ApplyFixAction(note.FixAction, note.SceneId);
        }

        /// <summary>
        /// Non-text fixes: "tag-character:&lt;id&gt;" / "untag-character:&lt;id&gt;" add or
        /// remove a character from the scene's cast. The scene is re-checked right away,
        /// so the note disappears once it no longer applies.
        /// </summary>
        internal void ApplyFixAction(string fixAction, string? sceneId)
        {
            var parts = fixAction.Split(':', 2);
            if (parts.Length != 2 || SelectedBook is null || string.IsNullOrEmpty(sceneId)) return;
            var scene = SelectedBook.Chapters.SelectMany(c => c.Scenes).FirstOrDefault(s => s.Id == sceneId);
            if (scene is null) return;

            switch (parts[0])
            {
                case "tag-character" when !scene.CharacterIds.Contains(parts[1]):
                    scene.CharacterIds.Add(parts[1]);
                    scene.UntaggedIds.Remove(parts[1]);
                    break;
                case "untag-character":
                    scene.CharacterIds.Remove(parts[1]);
                    if (!scene.UntaggedIds.Contains(parts[1])) scene.UntaggedIds.Add(parts[1]);   // stays untagged
                    break;
                default:
                    return;
            }

            if (SelectedScene == scene) RebuildSceneEntityOptions();
            DebouncedSave();
            ScheduleLiveAnalysis(scene, 0);
        }

        private void OnEditorialPreferencesChanged()
        {
            ApplyNoteFilters();
            PopulateSceneNoteCounts(_allNotes);
            OnPropertyChanged(nameof(DismissedCount));
            OnPropertyChanged(nameof(DismissedSummary));
            AnnotationsChanged?.Invoke();
            DebouncedSave();
        }

        // ── Live analysis ─────────────────────────────────────────────────────

        private CancellationTokenSource? _liveCts;
        private int _liveVersion;

        /// <summary>
        /// Re-runs the fast scene rules for <paramref name="scene"/> after a short pause
        /// (debounced — each call cancels the previous one).
        /// </summary>
        public void ScheduleLiveAnalysis(Scene scene, int delayMs = 900)
        {
            var book = SelectedBook;
            if (book is null) return;
            var chapter = book.Chapters.FirstOrDefault(c => c.Scenes.Contains(scene));
            if (chapter is null) return;

            _liveCts?.Cancel();
            var cts = _liveCts = new CancellationTokenSource();
            int version = ++_liveVersion;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (delayMs > 0) await Task.Delay(delayMs, cts.Token);
                    var live = _nlpAnalysisService.AnalyzeSceneLive(scene, chapter, book);
                    if (live is null || cts.IsCancellationRequested) return;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (version != _liveVersion || SelectedBook != book) return;
                        ApplyLiveAnalysis(scene.Id, live);
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LIVE] analysis failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Merges a live result into the note set: notes from the rules that just ran are
        /// replaced; the scene's other notes (POS, model, book-level) are moved onto the new
        /// text, and dropped if the sentence they pointed at has been rewritten.
        /// </summary>
        internal void ApplyLiveAnalysis(string sceneId, LiveSceneAnalysis live)
        {
            var kept = _allNotes
                .Where(n => n.SceneId == sceneId && !live.CoveredRuleIds.Contains(n.RuleId))
                .Where(n => NoteAnchoring.Reanchor(n, live.Document))
                .ToList();

            _allNotes = _allNotes
                .Where(n => n.SceneId != sceneId)
                .Concat(kept)
                .Concat(live.Result.Notes)
                .ToList();

            var result = _analysisResults?.FirstOrDefault(r => r.SceneId == sceneId);
            if (result is not null)
            {
                result.Notes = _allNotes.Where(n => n.SceneId == sceneId).ToList();
                // Keep the scene's report card current while the author edits
                result.Style = live.Result.Style;
                result.ParagraphBeats = live.Result.ParagraphBeats;
                result.Senses = live.Result.Senses;
                result.Tension = live.Result.Tension;
            }

            ApplyNoteFilters();
            PopulateSceneNoteCounts(_allNotes);
            AnnotationsChanged?.Invoke();
        }

        // ── Editor annotations ────────────────────────────────────────────────

        /// <summary>
        /// JSON for the editor's annotation layer: visible notes for one scene that point at
        /// specific words, plus every span to underline. Spans are located in the editor by
        /// their anchor sentence text, so they land on the exact words even in edited text.
        /// </summary>
        public string BuildAnnotationsJson(string? sceneId)
        {
            if (string.IsNullOrEmpty(sceneId)) return "null";

            // During a pass, only that rule's marks: one kind of problem at a time
            var notes = VisibleNotes
                .Where(n => n.SceneId == sceneId && !string.IsNullOrEmpty(n.AnchorText))
                .Where(n => !IsPassActive || n.RuleId == ActivePassRuleId)
                .ToList();

            var noteDtos = new List<object>();
            var spans = new List<object>();

            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                var rule = RuleCatalog.Find(n.RuleId);
                noteDtos.Add(new
                {
                    id = i,
                    ruleId = n.RuleId,
                    title = rule?.Title ?? n.CategoryLabel,
                    category = n.Category switch
                    {
                        NlpNoteCategory.CopyEditor => "copy",
                        NlpNoteCategory.LineEditor => "line",
                        _ => "dev"
                    },
                    severity = n.Severity.ToString().ToLowerInvariant(),
                    message = n.Message,
                    evidence = n.Evidence,
                    fingerprint = n.Fingerprint,
                    suggestions = n.Suggestions,
                    fixAction = n.FixAction,
                    fixActionLabel = n.FixActionLabel,
                    canAllowWord = !string.IsNullOrEmpty(n.Evidence) && !n.Evidence.Contains(' ')
                });

                spans.Add(new
                {
                    note = i,
                    anchor = n.AnchorText,
                    occ = n.AnchorOccurrence,
                    offset = n.AnchorOffset ?? 0,
                    // Sentence-level notes without a word span highlight the whole sentence
                    length = n.AnchorOffset.HasValue ? n.Length ?? n.AnchorText.Length : n.AnchorText.Length,
                    primary = true
                });
                foreach (var extra in n.ExtraSpans.Where(s => !string.IsNullOrEmpty(s.AnchorText)))
                {
                    spans.Add(new
                    {
                        note = i,
                        anchor = extra.AnchorText,
                        occ = extra.AnchorOccurrence,
                        offset = extra.AnchorOffset,
                        length = extra.Length,
                        primary = false
                    });
                }
            }

            return JsonSerializer.Serialize(new { notes = noteDtos, spans }, CamelCase);
        }

        /// <summary>JSON describing where a note sits, for the editor's focus call.</summary>
        public static string BuildFocusJson(NlpNote note) =>
            JsonSerializer.Serialize(new
            {
                fingerprint = note.Fingerprint,
                anchor = note.AnchorText,
                occ = note.AnchorOccurrence,
                offset = note.AnchorOffset ?? 0,
                length = note.AnchorOffset.HasValue ? note.Length ?? note.AnchorText.Length : note.AnchorText.Length
            }, CamelCase);

        /// <summary>Finds the best note to focus for a (scene, sentence) link from the report.</summary>
        public NlpNote? FindNoteForSentence(string sceneId, int? sentenceIndex)
        {
            var candidates = _allNotes.Where(n => n.SceneId == sceneId && !string.IsNullOrEmpty(n.AnchorText));
            return sentenceIndex is int idx
                ? candidates.FirstOrDefault(n => n.SentenceIndex == idx)
                : null;
        }
    }
}
