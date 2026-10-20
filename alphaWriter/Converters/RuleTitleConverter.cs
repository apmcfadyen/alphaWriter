using System.Globalization;
using alphaWriter.Services.Nlp.Rules;

namespace alphaWriter.Converters
{
    /// <summary>Rule id ("style.echo") → the rule's human title ("Word echoes").</summary>
    public class RuleTitleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string id && RuleCatalog.Find(id) is { } rule ? rule.Title : "Editorial note";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
