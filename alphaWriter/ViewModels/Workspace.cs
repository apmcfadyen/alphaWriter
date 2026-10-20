namespace alphaWriter.ViewModels
{
    /// <summary>
    /// The author's current job. Each workspace owns its sidebar and main area, and the
    /// rail always shows which one is active.
    /// </summary>
    public enum Workspace
    {
        /// <summary>Outline + editor + scene inspector.</summary>
        Write,
        /// <summary>Characters, locations and items.</summary>
        StoryBible,
        /// <summary>Editorial analysis and notes.</summary>
        Revise,
        /// <summary>Statistics and reports.</summary>
        Insights,
        /// <summary>Book details and word target.</summary>
        Book
    }
}
