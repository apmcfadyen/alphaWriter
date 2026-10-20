using System.Globalization;

namespace alphaWriter.Converters
{
    /// <summary>
    /// true → 1, false → a dimmed opacity (0.4 by default, or the ConverterParameter).
    /// Used to show unavailable items as present-but-inactive rather than hiding them.
    /// </summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double dimmed = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0.4;
            return value is true ? 1.0 : dimmed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InvertBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    }
}
