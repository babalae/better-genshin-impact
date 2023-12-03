using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(double), typeof(bool))]
public class DoubleToBoolConverter : ValueToBoolConverter<double>
{
}
