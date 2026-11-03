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
            if (_viewModel is not null)
            {
                _viewModel.ExportPanelRequested -= ScrollToExport;
                _viewModel.SyncPanelRequested -= ScrollToSync;
            }
            _viewModel = BindingContext as WriterViewModel;
            if (_viewModel is not null)
            {
                _viewModel.ExportPanelRequested += ScrollToExport;
                _viewModel.SyncPanelRequested += ScrollToSync;
            }
        }

        /// <summary>"Export chapter…" lands on the export settings, not the top of the page.</summary>
        private void ScrollToExport() => ScrollTo(ExportHeader);

        /// <summary>The status bar's sync indicator lands on the sync settings.</summary>
        private void ScrollToSync() => ScrollTo(SyncHeader);

        private void ScrollTo(VisualElement target) =>
            Dispatcher.Dispatch(async () =>
            {
                // Let the workspace switch lay out first (longer when the app is still starting)
                for (int i = 0; i < 40 && (target.Height <= 0 || Scroller.ContentSize.Height <= Scroller.Height); i++)
                    await Task.Delay(50);
                await Task.Delay(50);
                await Scroller.ScrollToAsync(target, ScrollToPosition.Start, false);
            });

        private void OnBookInfoFieldUnfocused(object? sender, FocusEventArgs e) =>
            (BindingContext as WriterViewModel)?.SaveBookInfo();
    }
}
