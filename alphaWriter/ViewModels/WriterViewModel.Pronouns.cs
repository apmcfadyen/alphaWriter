using alphaWriter.Models;
using alphaWriter.Services.Nlp.Rules;

namespace alphaWriter.ViewModels
{
    /// <summary>Story Bible: each character's pronouns, used to resolve "he" and "she" in the prose.</summary>
    public partial class WriterViewModel
    {
        public const string PronounsNotSet = "Not set (guess from text)";

        public IReadOnlyList<string> PronounOptions { get; } =
            [PronounsNotSet, .. PronounSets.Options.Where(o => o.Length > 0)];

        /// <summary>Picker value for the selected character ("Not set" ↔ empty).</summary>
        public string SelectedCharacterPronouns
        {
            get => string.IsNullOrEmpty(SelectedCharacter?.Pronouns) ? PronounsNotSet : SelectedCharacter.Pronouns;
            set
            {
                if (SelectedCharacter is null || value is null) return;
                var stored = value == PronounsNotSet ? string.Empty : value;
                if (SelectedCharacter.Pronouns == stored) return;
                SelectedCharacter.Pronouns = stored;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedCharacterPronounHint));
                SaveElementDetails();
                // Pronoun-aware rules on the open scene should reflect the change right away
                if (SelectedScene is not null) ScheduleLiveAnalysis(SelectedScene, 0);
            }
        }

        /// <summary>When pronouns aren't set, what the text suggests (or that it can't tell yet).</summary>
        public string SelectedCharacterPronounHint
        {
            get
            {
                if (SelectedCharacter is null || SelectedBook is null || !string.IsNullOrEmpty(SelectedCharacter.Pronouns))
                    return string.Empty;
                var guess = PronounInference.Guess(SelectedCharacter, SelectedBook);
                return guess == PronounSet.Unknown
                    ? "Not enough text to guess yet. Setting this helps the editor spot ambiguous \u201Che\u201D and \u201Cshe\u201D."
                    : $"Guessed from your text: {PronounSets.Label(guess)}. Set it to confirm.";
            }
        }

        private void NotifyPronounsChanged()
        {
            OnPropertyChanged(nameof(SelectedCharacterPronouns));
            OnPropertyChanged(nameof(SelectedCharacterPronounHint));
        }
    }
}
