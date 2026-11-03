using alphaWriter.ViewModels;

namespace alphaWriter.Views.Workspaces
{
    public partial class BookMain : ContentView
    {
        private WriterViewModel? _viewModel;

        public BookMain()
        {
            InitializeComponent();
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (_viewModel is not null) _viewModel.ExportPanelRequested -= ScrollToExport;
            _viewModel = BindingContext as WriterViewModel;
            if (_viewModel is not null) _viewModel.ExportPanelRequested += ScrollToExport;
        }

        /// <summary>"Export chapter…" lands on the export settings, not the top of the page.</summary>
        private void ScrollToExport() =>
            Dispatcher.Dispatch(async () =>
            {
                await Task.Delay(50);   // let the workspace switch lay out first
                await Scroller.ScrollToAsync(ExportHeader, ScrollToPosition.Start, false);
            });

        private void OnBookInfoFieldUnfocused(object? sender, FocusEventArgs e) =>
            (BindingContext as WriterViewModel)?.SaveBookInfo();
    }
}
