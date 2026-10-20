namespace alphaWriter.Views
{
    /// <summary>
    /// Shared visuals for drag-to-reorder lists. Rows are Grids whose first and last
    /// children are thin BoxViews used as "drop here" indicators above/below the row.
    /// </summary>
    internal static class DragDropHelpers
    {
        /// <summary>True when the drag cursor is in the lower half of the hovered element.</summary>
        public static bool IsCursorInBottomHalf(object? sender, DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs is { } nativeArgs &&
                sender is VisualElement ve &&
                ve.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeEl)
            {
                var pt = nativeArgs.GetPosition(nativeEl);
                return pt.Y > nativeEl.ActualHeight / 2.0;
            }
#endif
            return false;
        }

        /// <summary>Shows the top (or, with <paramref name="atBottom"/>, the bottom) indicator.</summary>
        public static void ShowDropIndicator(object? sender, bool atBottom = false)
        {
            if (sender is not Grid g || g.Children.Count < 2) return;
            var top = g.Children[0] as BoxView;
            var bot = g.Children[^1] as BoxView;
            if (top != null) top.IsVisible = !atBottom;
            if (bot != null) bot.IsVisible = atBottom;
        }

        public static void HideDropIndicator(object? sender)
        {
            if (sender is not Grid g) return;
            if (g.Children.Count > 0 && g.Children[0] is BoxView top) top.IsVisible = false;
            if (g.Children.Count > 1 && g.Children[^1] is BoxView bot) bot.IsVisible = false;
        }

        /// <summary>Suppresses the platform "Copy" callout/glyph beside the cursor.</summary>
        public static void SuppressDragCallout(DragEventArgs e)
        {
#if WINDOWS
            if (e.PlatformArgs is PlatformDragEventArgs pd &&
                pd.DragEventArgs?.DragUIOverride is { } ui)
            {
                ui.IsGlyphVisible = false;
                ui.IsCaptionVisible = false;
                ui.IsContentVisible = false;
            }
#endif
        }
    }
}
