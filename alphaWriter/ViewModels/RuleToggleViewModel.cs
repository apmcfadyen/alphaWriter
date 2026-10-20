using alphaWriter.Services.Nlp.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

namespace alphaWriter.ViewModels
{
    /// <summary>One row in the editorial settings panel: a rule and whether it is on.</summary>
    public partial class RuleToggleViewModel : ObservableObject
    {
        private readonly Action<RuleToggleViewModel>? _onChanged;

        public RuleToggleViewModel(RuleDescriptor descriptor, bool isEnabled,
            Action<RuleToggleViewModel>? onChanged = null)
        {
            Descriptor = descriptor;
            this.isEnabled = isEnabled;
            _onChanged = onChanged;
        }

        public RuleDescriptor Descriptor { get; }
        public string Id => Descriptor.Id;
        public string Title => Descriptor.Title;
        public string Description => Descriptor.Description;

        public string CategoryLabel => Descriptor.Category switch
        {
            Models.Analysis.NlpNoteCategory.CopyEditor => "Copy",
            Models.Analysis.NlpNoteCategory.LineEditor => "Line",
            _ => "Developmental"
        };

        /// <summary>When the rule runs: while typing, or only on a full analysis.</summary>
        public string TierLabel => Descriptor.Tier switch
        {
            RuleTier.Lexical when Descriptor.Scope == RuleScope.Scene => "live",
            RuleTier.Model => "needs models",
            _ => "full analysis"
        };

        [ObservableProperty]
        private bool isEnabled;

        partial void OnIsEnabledChanged(bool value) => _onChanged?.Invoke(this);
    }
}
