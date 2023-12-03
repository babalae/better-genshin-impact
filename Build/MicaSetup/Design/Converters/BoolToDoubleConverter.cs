using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(double))]
public class BoolToDoubleConverter : BoolToValueConverter<double>
{
}
