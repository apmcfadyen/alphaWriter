using alphaWriter.ViewModels;

namespace alphaWriter.Views.Workspaces
{
    public partial class SceneInspector : ContentView
    {
        public SceneInspector()
        {
            InitializeComponent();
        }

        private void OnFieldUnfocused(object? sender, FocusEventArgs e) =>
            (BindingContext as WriterViewModel)?.SaveSceneMetadata();
    }
}
