using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace alphaWriter.ViewModels
{
    public class BreadcrumbItem : ObservableObject
    {
        private string _label = string.Empty;
        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        private string _level = string.Empty;
        public string Level
        {
            get => _level;
            set => SetProperty(ref _level, value);
        }

        public ICommand? Command { get; set; }
    }
}
