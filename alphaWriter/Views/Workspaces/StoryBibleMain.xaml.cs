using alphaWriter.ViewModels;

namespace alphaWriter.Views.Workspaces
{
    public partial class StoryBibleMain : ContentView
    {
        public StoryBibleMain()
        {
            InitializeComponent();
        }

        private void OnElementFieldUnfocused(object? sender, FocusEventArgs e) =>
            (BindingContext as WriterViewModel)?.SaveElementDetails();
    }
}
