using System.Globalization;
using alphaWriter.Models;

namespace alphaWriter.Converters
{
    public class SceneStatusDisplayConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is SceneStatus status)
            {
                return status switch
                {
                    SceneStatus.Outline => "Outline",
                    SceneStatus.Draft => "Draft",
                    SceneStatus.FirstEdit => "1st Edit",
                    SceneStatus.SecondEdit => "2nd Edit",
                    SceneStatus.Done => "Done",
                    _ => status.ToString(),
                };
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
