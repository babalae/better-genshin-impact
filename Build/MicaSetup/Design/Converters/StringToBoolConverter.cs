using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(string), typeof(bool))]
public class StringToBoolConverter : ValueToBoolConverter<string>
{
}
