using System.Windows.Data;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(bool))]
public class InvertedBoolConverter : BoolNegationConverter
{
}
