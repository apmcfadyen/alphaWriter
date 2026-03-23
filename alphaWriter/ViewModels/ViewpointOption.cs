using CommunityToolkit.Mvvm.ComponentModel;
using alphaWriter.Models;

namespace alphaWriter.ViewModels
{
    public partial class ViewpointOption : ObservableObject
    {
        public Character Character { get; }

        [ObservableProperty]
        private bool isSelected;

        public ViewpointOption(Character character, bool isSelected)
        {
            Character = character;
            this.isSelected = isSelected;
        }
    }
}
