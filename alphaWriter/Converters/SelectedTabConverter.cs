using System.Globalization;

namespace alphaWriter.Converters
{
    /// <summary>
    /// Colors a tab, rail item or list row by whether it is the active one, so the
    /// current mode/location is always visible.
    /// </summary>
    /// <remarks>
    /// The bound value is either a bool (selected or not) or any value compared to a key.
    /// ConverterParameter is a ';'-separated list:
    /// <list type="bullet">
    /// <item>a key (e.g. "Write") — selected when the value's text equals it;</item>
    /// <item>"text" — return the text color instead of the background;</item>
    /// <item>"row" — unselected background is transparent (for list rows).</item>
    /// </list>
    /// Examples: <c>ConverterParameter=text</c>, <c>ConverterParameter='Write;text'</c>.
    /// </remarks>
    public class SelectedTabConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var parts = (parameter as string ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool text = parts.Contains("text", StringComparer.OrdinalIgnoreCase);
            bool row = parts.Contains("row", StringComparer.OrdinalIgnoreCase);
            var key = parts.FirstOrDefault(p =>
                !p.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("row", StringComparison.OrdinalIgnoreCase));

            bool selected = key is null
                ? value is true
                : string.Equals(value?.ToString(), key, StringComparison.Ordinal);

            if (!text && !selected && row) return Colors.Transparent;

            string resource = text
                ? (selected ? "TextPrimary" : "TextSecondary")
                : (selected ? "SurfaceSelected" : "SurfaceRaised");
            return Application.Current?.Resources.TryGetValue(resource, out var color) == true
                ? color
                : Colors.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
