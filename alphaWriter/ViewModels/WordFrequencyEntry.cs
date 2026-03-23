namespace alphaWriter.ViewModels
{
    public class WordFrequencyEntry
    {
        public string Word { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }

        // Pre-normalised 0–1 value for the ProgressBar, relative to the collection maximum.
        // Set after construction by the ViewModel.
        public double ProgressValue { get; set; }
    }
}
