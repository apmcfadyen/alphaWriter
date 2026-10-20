namespace alphaWriter.ViewModels
{
    public class WordFrequencyEntry
    {
        public string Word { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }

        /// <summary>Right-hand detail text: "12.4%" or "11× typical".</summary>
        public string Detail { get; set; } = string.Empty;

        // Pre-normalised 0–1 value for the ProgressBar, relative to the collection maximum.
        // Set after construction by the ViewModel.
        public double ProgressValue { get; set; }
    }
}
