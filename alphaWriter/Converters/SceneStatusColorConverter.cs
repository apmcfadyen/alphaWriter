using System.Globalization;
using alphaWriter.Models;

namespace alphaWriter.Converters
{
    public class SceneStatusColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is SceneStatus status)
            {
                return status switch
                {
                    SceneStatus.Outline => Color.FromArgb("#B05060"),    // red
                    SceneStatus.Draft => Color.FromArgb("#C0A030"),      // yellow
                    SceneStatus.FirstEdit => Color.FromArgb("#5090C0"),  // blue
                    SceneStatus.SecondEdit => Color.FromArgb("#6080B0"), // steel
                    SceneStatus.Done => Color.FromArgb("#50A060"),       // green
                    _ => Color.FromArgb("#5A5A6A"),
                };
            }
            return Color.FromArgb("#5A5A6A");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
