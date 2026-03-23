namespace alphaWriter.ViewModels
{
    public class DailyProgressEntry
    {
        public string Date { get; set; } = string.Empty;
        public int Delta { get; set; }
        public int Total { get; set; }

        public string DisplayDate
        {
            get
            {
                if (DateTime.TryParse(Date, out var d))
                {
                    if (d.Date == DateTime.Today) return "Today";
                    if (d.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
                    return d.ToString("ddd, MMM d");
                }
                return Date;
            }
        }

        public string DeltaText => Delta >= 0 ? $"+{Delta:N0}" : $"{Delta:N0}";

        public Color DeltaColor => Delta >= 0
            ? Color.FromArgb("#4CAF50")
            : Color.FromArgb("#F44336");
    }
}
