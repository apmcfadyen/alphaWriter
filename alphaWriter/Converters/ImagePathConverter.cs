using System.Globalization;

namespace alphaWriter.Converters
{
    public class ImagePathConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                var fullPath = Path.Combine(FileSystem.AppDataDirectory, path);
                if (File.Exists(fullPath))
                    return ImageSource.FromFile(fullPath);
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
