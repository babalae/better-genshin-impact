using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(string))]
public class BoolToStringConverter : BoolToValueConverter<string>
{
}
