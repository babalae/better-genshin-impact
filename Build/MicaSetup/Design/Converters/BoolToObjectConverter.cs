using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(object))]
public class BoolToObjectConverter : BoolToValueConverter<object>
{
}
