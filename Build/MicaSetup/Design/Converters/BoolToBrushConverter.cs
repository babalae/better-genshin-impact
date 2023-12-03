using System.Windows.Data;
using System.Windows.Media;

namespace MicaSetup.Design.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToBrushConverter : BoolToValueConverter<Brush>
{
}
