using alphaWriter.ViewModels;

namespace alphaWriter.Views.Workspaces
{
    public partial class BookMain : ContentView
    {
        public BookMain()
        {
            InitializeComponent();
        }

        private void OnBookInfoFieldUnfocused(object? sender, FocusEventArgs e) =>
            (BindingContext as WriterViewModel)?.SaveBookInfo();
    }
}
