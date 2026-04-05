using alphaWriter.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace alphaWriter.ViewModels
{
    public partial class NerCandidateViewModel : ObservableObject
    {
        public string Name { get; init; } = string.Empty;
        public NerEntityType Type { get; init; }
        public int Count { get; init; }

        [ObservableProperty]
        private bool isSelected = true;

        public string CountLabel => Count == 1 ? "found 1\u00d7" : $"found {Count}\u00d7";
    }
}
