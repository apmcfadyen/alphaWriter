using alphaWriter.Models;
using alphaWriter.Models.Analysis;
using alphaWriter.Services.Nlp.Rules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace alphaWriter.ViewModels
{
    /// <summary>One rule the author can work through as a revision pass.</summary>
    public sealed record PassOption(string RuleId, string Title, string Description, string CategoryColor,
        int Count, string Trend)
    {
        public string CountText => Count == 1 ? "1 note" : $"{Count} notes";
        public bool HasTrend => Trend.Length > 0;
    }

    /// <summary>
    /// Revision workflow: stepping through notes one at a time (F8 / Shift+F8), focused
    /// passes that follow a single rule through the whole book, and note counts kept from
    /// run to run so progress is visible.
    /// </summary>
    public partial class WriterViewModel
    {
        private const int MaxHistory = 30;

        // ── Stepping through notes ────────────────────────────────────────────

        /// <summary>The note most recently opened from Revise, the report or F8.</summary>
        private NlpNote? _currentNote;

        /// <summary>Where <see cref="_currentNote"/> sat in reading order, kept after it is fixed.</summary>
        private (int Scene, int Offset) _currentPosition = (-1, -1);

        /// <summary>Scene id → position in the book, for ordering notes as the text reads.</summary>
        private Dictionary<string, int> SceneOrder()
        {
            var order = new Dictionary<string, int>();
            if (SelectedBook is null) return order;
            int i = 0;
            foreach (var chapter in SelectedBook.Chapters)
                foreach (var scene in chapter.Scenes)
                    order[scene.Id] = i++;
            return order;
        }

        private static (int Scene, int Offset) PositionOf(NlpNote note, Dictionary<string, int> order) =>
            (order.GetValueOrDefault(note.SceneId, -1), note.Start ?? -1);

        /// <summary>Notes F8 walks through: the active pass, or else the Revise list, in reading order.</summary>
        private List<NlpNote> SteppableNotes()
        {
            var order = SceneOrder();
            var notes = IsPassActive
                ? VisibleNotes.Where(n => n.RuleId == ActivePassRuleId)
                : NlpNotes.AsEnumerable();
            return notes
                .Where(n => order.ContainsKey(n.SceneId))
                .OrderBy(n => PositionOf(n, order))
                .ToList();
        }

        /// <summary>Opens the next (or previous) note after the current one, wrapping at the ends.</summary>
        internal void StepNote(int direction)
        {
            var notes = SteppableNotes();
            if (notes.Count == 0) return;
            var order = SceneOrder();

            int index;
            if (IndexOfCurrent(notes) is var at and >= 0)
                index = (at + direction + notes.Count) % notes.Count;
            else if (_currentPosition.Scene < 0)
                index = direction > 0 ? 0 : notes.Count - 1;
            else
            {
                // The current note is gone (fixed or ignored): continue from where it was
                index = direction > 0
                    ? notes.FindIndex(n => PositionOf(n, order).CompareTo(_currentPosition) > 0)
                    : notes.FindLastIndex(n => PositionOf(n, order).CompareTo(_currentPosition) < 0);
                if (index < 0) index = direction > 0 ? 0 : notes.Count - 1;
            }
            OpenNote(notes[index]);
        }

        /// <summary>
        /// Where the current note is in <paramref name="notes"/>. Matched by fingerprint, because
        /// live re-analysis replaces note objects while the author edits.
        /// </summary>
        private int IndexOfCurrent(List<NlpNote> notes) =>
            _currentNote is null ? -1
            : string.IsNullOrEmpty(_currentNote.Fingerprint) ? notes.IndexOf(_currentNote)
            : notes.FindIndex(n => n.Fingerprint == _currentNote.Fingerprint);

        /// <summary>Remembers a note as current (called whenever a note is opened).</summary>
        private void TrackCurrentNote(NlpNote note)
        {
            _currentNote = note;
            _currentPosition = PositionOf(note, SceneOrder());
            UpdatePassProgress();
        }

        private void OpenNote(NlpNote note) => NavigateToNote(note);

        [RelayCommand]
        private void NextNote() => StepNote(+1);

        [RelayCommand]
        private void PreviousNote() => StepNote(-1);

        // ── Passes ────────────────────────────────────────────────────────────

        /// <summary>Rules that currently have notes, as passes to choose from.</summary>
        [ObservableProperty]
        private ObservableCollection<PassOption> passOptions = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotesListVisible))]
        private bool isPassPickerVisible;

        /// <summary>The rule being worked through, or null when no pass is running.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPassActive))]
        [NotifyPropertyChangedFor(nameof(IsBrowsingNotes))]
        [NotifyPropertyChangedFor(nameof(ActivePassTitle))]
        [NotifyPropertyChangedFor(nameof(ActivePassDescription))]
        private string? activePassRuleId;

        public bool IsPassActive => ActivePassRuleId is not null;

        /// <summary>The normal Revise list (scope and filters) rather than a pass.</summary>
        public bool IsBrowsingNotes => ActivePassRuleId is null;

        public string ActivePassTitle => ActivePassRuleId is null ? string.Empty
            : RuleCatalog.Find(ActivePassRuleId)?.Title ?? ActivePassRuleId;

        public string ActivePassDescription => ActivePassRuleId is null ? string.Empty
            : RuleCatalog.Find(ActivePassRuleId)?.Description ?? string.Empty;

        [ObservableProperty]
        private string passProgressText = string.Empty;

        /// <summary>The note the pass is on, shown in the pass card.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasCurrentPassNote))]
        private NlpNote? currentPassNote;

        public bool HasCurrentPassNote => CurrentPassNote is not null;

        private int _passStartCount;

        [RelayCommand]
        private void TogglePassPicker()
        {
            if (!IsPassPickerVisible) RebuildPassOptions();
            IsPassPickerVisible = !IsPassPickerVisible;
            if (IsPassPickerVisible) IsRuleSettingsVisible = false;
        }

        internal void RebuildPassOptions()
        {
            var previous = SelectedBook?.AnalysisHistory.Count >= 2
                ? SelectedBook.AnalysisHistory[^2]
                : null;
            var options = VisibleNotes
                .Where(n => !string.IsNullOrEmpty(n.SceneId))
                .GroupBy(n => n.RuleId)
                .Select(g =>
                {
                    var rule = RuleCatalog.Find(g.Key);
                    var first = g.First();
                    string trend = previous is not null && previous.ByRule.TryGetValue(g.Key, out var before) && before != g.Count()
                        ? $"was {before}"
                        : string.Empty;
                    return new PassOption(g.Key, rule?.Title ?? first.CategoryLabel, rule?.Description ?? string.Empty,
                        first.CategoryColor, g.Count(), trend);
                })
                .OrderBy(o => RuleCatalog.Find(o.RuleId)?.Category ?? NlpNoteCategory.LineEditor)
                .ThenByDescending(o => o.Count);
            PassOptions = new ObservableCollection<PassOption>(options);
        }

        [RelayCommand]
        private void StartPass(PassOption? option)
        {
            if (option is null) return;
            IsPassPickerVisible = false;
            ActivePassRuleId = option.RuleId;
            ReviseScopeIsScene = false;
            _passStartCount = SteppableNotes().Count;
            _currentNote = null;
            _currentPosition = (-1, -1);
            RebuildReviseGroups();
            AnnotationsChanged?.Invoke();
            StepNote(+1);
        }

        [RelayCommand]
        private void EndPass()
        {
            ActivePassRuleId = null;
            CurrentPassNote = null;
            PassProgressText = string.Empty;
            RebuildReviseGroups();
            AnnotationsChanged?.Invoke();
        }

        /// <summary>Ignores the pass's current note and moves on.</summary>
        [RelayCommand]
        private void IgnorePassNote()
        {
            if (CurrentPassNote is not { } note) return;
            DismissNote(note);
            StepNote(+1);
        }

        /// <summary>Applies the current note's one-click fix and moves on.</summary>
        [RelayCommand]
        private void FixPassNote()
        {
            if (CurrentPassNote is not { HasFixAction: true } note) return;
            ApplyFixAction(note.FixAction, note.SceneId);
            StepNote(+1);
        }

        /// <summary>Refreshes the pass card; ends the pass when nothing is left.</summary>
        internal void UpdatePassProgress()
        {
            if (!IsPassActive) return;
            var notes = SteppableNotes();
            if (notes.Count == 0)
            {
                CurrentPassNote = null;
                PassProgressText = _passStartCount > 0
                    ? $"All {_passStartCount} handled. Nice work."
                    : "Nothing left in this pass.";
                return;
            }
            int index = IndexOfCurrent(notes);
            CurrentPassNote = index >= 0 ? notes[index] : null;
            int handled = Math.Max(0, _passStartCount - notes.Count);
            PassProgressText = (index >= 0 ? $"Note {index + 1} of {notes.Count}" : $"{notes.Count} left")
                + (handled > 0 ? $" · {handled} handled" : string.Empty);
        }

        // ── History ───────────────────────────────────────────────────────────

        /// <summary>Records this run's visible note counts on the book.</summary>
        internal void RecordAnalysisSnapshot()
        {
            if (SelectedBook is null) return;
            var visible = VisibleNotes.ToList();
            var history = SelectedBook.AnalysisHistory;
            history.Add(new AnalysisSnapshot
            {
                Date = DateTime.Now,
                Words = SelectedBook.WordCount,
                Total = visible.Count,
                ByRule = visible.GroupBy(n => n.RuleId).ToDictionary(g => g.Key, g => g.Count())
            });
            if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);
            OnPropertyChanged(nameof(ProgressSummary));
        }

        /// <summary>"Down 9 since Oct 18 (31 → 22)", comparing the last two runs.</summary>
        public string ProgressSummary
        {
            get
            {
                var history = SelectedBook?.AnalysisHistory;
                if (history is null || history.Count < 2) return string.Empty;
                var (before, now) = (history[^2], history[^1]);
                int change = now.Total - before.Total;
                var since = before.Date.ToString("MMM d");
                return change switch
                {
                    < 0 => $"Down {-change} since {since} ({before.Total} → {now.Total})",
                    > 0 => $"Up {change} since {since} ({before.Total} → {now.Total})",
                    _   => $"Same as {since} ({now.Total})"
                };
            }
        }

        public bool HasProgressSummary => ProgressSummary.Length > 0;
    }
}
