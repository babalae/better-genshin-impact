using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public class IntegerToBoolConverter : ValueToBoolConverter<int>
{
}
